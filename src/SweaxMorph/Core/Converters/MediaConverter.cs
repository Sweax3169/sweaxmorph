using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// Video, audio and subtitles through FFmpeg. Streams are copied instead of re-encoded whenever the new
/// container can hold them (MP4 → MKV takes a second), and everything else uses tuned encoder settings.
/// </summary>
public sealed class MediaConverter : Converter
{
    public override string Name => "FFmpeg";
    public override Engine Engine => Engines.FFmpeg;

    static readonly string[] VideoIn = ["mp4", "mkv", "webm", "mov", "avi", "wmv", "flv", "m4v", "mpg", "ts", "3gp", "ogv", "gif", "apng"];
    static readonly string[] AudioIn = ["mp3", "wav", "flac", "m4a", "aac", "ogg", "opus", "wma", "aiff", "ac3", "m4r", "mka", "wv", "ape", "amr", "au", "dts"];
    static readonly string[] SubIn = ["srt", "vtt", "ass", "ttml", "lrc", "sub"];
    static readonly string[] SubOut = ["srt", "vtt", "ass", "ttml", "lrc"];

    static HashSet<string>? _encoders;
    static string? _encodersFor;

    /// <summary>The encoders in this FFmpeg build (cached per binary).</summary>
    static HashSet<string> Encoders()
    {
        string? exe = Engines.FFmpeg.Path;
        if (exe == null) return [];
        if (_encoders != null && _encodersFor == exe) return _encoders;
        var set = new HashSet<string>();
        try
        {
            var r = Task.Run(() => Proc.Run(exe, ["-hide_banner", "-encoders"], CancellationToken.None, timeout: TimeSpan.FromSeconds(15))).GetAwaiter().GetResult();
            foreach (var line in r.Out.Split('\n'))
            {
                var m = Regex.Match(line, @"^\s[VASFXBD\.]{6}\s+(\S+)");
                if (m.Success) set.Add(m.Groups[1].Value);
            }
        }
        catch (Exception ex) { Paths.Log("ffmpeg encoders: " + ex.Message); }
        _encodersFor = exe;
        return _encoders = set;
    }

    /// <summary>Formats this FFmpeg can write. When FFmpeg still has to be downloaded, assume the usual build.</summary>
    static IEnumerable<string> VideoOut(HashSet<string> enc, bool assume)
    {
        bool Has(string e) => assume || enc.Contains(e);
        if (Has("libx264")) { yield return "mp4"; yield return "mov"; yield return "m4v"; yield return "mkv"; yield return "flv"; yield return "ts"; yield return "3gp"; }
        if (Has("libvpx-vp9")) yield return "webm";
        if (Has("mpeg4")) yield return "avi";
        if (Has("wmv2")) yield return "wmv";
        if (Has("mpeg2video")) yield return "mpg";
        if (Has("libtheora")) yield return "ogv";
        yield return "gif";
        if (Has("apng")) yield return "apng";
    }

    static IEnumerable<string> AudioOut(HashSet<string> enc, bool assume)
    {
        bool Has(string e) => assume || enc.Contains(e);
        if (Has("libmp3lame")) yield return "mp3";
        yield return "wav"; yield return "flac"; yield return "aiff"; yield return "au"; yield return "mka";
        if (Has("aac")) { yield return "m4a"; yield return "aac"; yield return "m4r"; }
        if (Has("libvorbis")) yield return "ogg";
        if (Has("libopus")) yield return "opus";
        if (Has("wmav2")) yield return "wma";
        if (Has("ac3")) yield return "ac3";
        if (Has("wavpack")) yield return "wv";
        if (enc.Contains("libopencore_amrnb")) yield return "amr";
    }

    public override IEnumerable<Edge> Edges()
    {
        var enc = Encoders();
        bool assume = !Engines.FFmpeg.Ready;
        var vOut = VideoOut(enc, assume).ToArray();
        var aOut = AudioOut(enc, assume).ToArray();

        foreach (var e in Cross(VideoIn, vOut, 2)) yield return e;
        foreach (var e in Cross(VideoIn.Where(v => v is not "gif" and not "apng"), aOut, 2)) yield return e;
        foreach (var e in Cross(AudioIn, aOut, 2)) yield return e;
        // a song becomes a video with its waveform dancing, ready for YouTube or Instagram
        foreach (var e in Cross(AudioIn, vOut.Where(v => v is "mp4" or "webm" or "mov" or "mkv"), 4)) yield return e;
        // one still frame out of a video
        foreach (var e in Cross(VideoIn.Where(v => v is not "gif" and not "apng"), ["png", "jpg"], 3)) yield return e;
        if (assume || enc.Contains("libwebp_anim") || enc.Contains("libwebp"))
            foreach (var e in Cross(VideoIn, ["webp"], 3)) yield return e;
        // subtitles: convert, or pull them out of a video
        foreach (var e in Cross(SubIn, SubOut, 1)) yield return e;
        foreach (var e in Cross(VideoIn.Where(v => v is "mkv" or "mp4" or "mov" or "m4v" or "webm" or "ts"), SubOut, 3)) yield return e;

        // smaller copies in the same format
        foreach (var f in new[] { "mp4", "mkv", "mov", "webm", "m4v", "gif" }.Where(vOut.Contains)) yield return new Edge(f, f, 2);
        foreach (var f in new[] { "mp3", "m4a", "ogg", "opus", "flac", "wma" }.Where(aOut.Contains)) yield return new Edge(f, f, 2);

        // many clips → one
        foreach (var f in AudioIn) foreach (var t in aOut.Where(t => t is "mp3" or "wav" or "m4a" or "flac")) yield return new Edge(f, t, 3, Combines: true);
        if (vOut.Contains("mp4")) foreach (var f in VideoIn) yield return new Edge(f, "mp4", 4, Combines: true);
    }

    // ── probing ─────────────────────────────────────────────────────────────

    sealed record Probe(double Duration, string? Video, string? Audio, int Width, int Height, int Subtitles, bool HasAudio);

    static async Task<Probe> Inspect(string file, CancellationToken ct)
    {
        if (Engines.FFprobe is { } probe)
        {
            try
            {
                var r = await Proc.Run(probe, ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,disposition", "-of", "json", file], ct, timeout: TimeSpan.FromSeconds(30));
                using var doc = JsonDocument.Parse(r.Out);
                double dur = 0;
                if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d))
                    double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
                string? v = null, a = null;
                int w = 0, h = 0, subs = 0;
                if (doc.RootElement.TryGetProperty("streams", out var streams))
                    foreach (var s in streams.EnumerateArray())
                    {
                        string type = s.GetProperty("codec_type").GetString() ?? "";
                        string name = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
                        bool cover = s.TryGetProperty("disposition", out var disp) && disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                        if (type == "video" && v == null && !cover) { v = name; w = s.TryGetProperty("width", out var ww) ? ww.GetInt32() : 0; h = s.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0; }
                        else if (type == "audio" && a == null) a = name;
                        else if (type == "subtitle") subs++;
                    }
                return new Probe(dur, v, a, w, h, subs, a != null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Paths.Log("ffprobe: " + ex.Message); }
        }
        return new Probe(0, null, null, 0, 0, 0, true);
    }

    // ── running ─────────────────────────────────────────────────────────────

    public override async Task<string[]> Run(Step s)
    {
        if (s.Inputs.Length > 1) return [await Concat(s)];
        var info = await Inspect(s.Input, s.Cancel);
        string to = s.To.Id, from = s.From.Id;
        bool fromAudio = AudioIn.Contains(from);
        string outFile = s.Out();

        if (SubOut.Contains(to))
        {
            if (!SubIn.Contains(from) && info.Subtitles == 0) throw new ConvertException(Loc.T("Err.NoSubs"));
            await Ffmpeg(s, info, ["-i", s.Input, .. SubIn.Contains(from) ? Array.Empty<string>() : ["-map", "0:s:0"], "-c:s", SubCodec(to), outFile]);
            return [outFile];
        }

        if (to is "png" or "jpg" && !fromAudio)
        {
            // a frame a third of the way in: past the intro, before the credits
            double at = info.Duration > 0 ? info.Duration / 3 : 0;
            await Ffmpeg(s, info, ["-ss", at.ToString("0.###", CultureInfo.InvariantCulture), "-i", s.Input, "-frames:v", "1", "-update", "1", .. to == "jpg" ? new[] { "-q:v", "2" } : [], outFile]);
            return [outFile];
        }

        var enc = Encoders();
        var q = from == to ? Quality.Small : s.Quality;

        // audio-only results
        if (AudioOut(enc, false).Contains(to) && !VideoOut(enc, false).Contains(to))
        {
            if (!info.HasAudio && info.Audio == null && Engines.FFprobe != null) throw new ConvertException(Loc.T("Err.NoAudio"));
            string[] args = ["-i", s.Input, "-map", "0:a:0", "-vn", "-map_metadata", "0", .. AudioArgs(to, q, info, enc)];
            if (to == "m4r") args = [.. args, "-t", "40"];
            await Ffmpeg(s, info, [.. args, outFile]);
            return [outFile];
        }

        if (fromAudio)
        {
            string[] codec = to == "webm"
                ? ["-c:v", "libvpx-vp9", "-crf", "36", "-b:v", "0", "-row-mt", "1", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus", "-b:a", "160k"]
                : ["-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", .. Container(to)];
            await Ffmpeg(s, info, ["-i", s.Input, "-filter_complex", Waveform(), "-map", "[v]", "-map", "0:a:0", .. codec, "-shortest", outFile]);
            return [outFile];
        }

        if (to is "gif" or "apng" or "webp")
        {
            var (fps, width) = q switch { Quality.Best => (20, 960), Quality.Balanced => (15, 720), _ => (12, 480) };
            string scale = $"fps={fps},scale='min({width},iw)':-2:flags=lanczos";
            string[] args = to switch
            {
                "gif" => ["-i", s.Input, "-filter_complex", $"[0:v]{scale},split[a][b];[a]palettegen=max_colors=256:stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle", "-loop", "0"],
                "apng" => ["-i", s.Input, "-vf", scale, "-f", "apng", "-plays", "0"],
                _ => ["-i", s.Input, "-vf", scale, "-c:v", enc.Contains("libwebp_anim") ? "libwebp_anim" : "libwebp", "-q:v", q == Quality.Best ? "88" : "75", "-loop", "0", "-an"],
            };
            await Ffmpeg(s, info, [.. args, outFile]);
            return [outFile];
        }

        // video → video: copy the streams when the container can carry them, otherwise encode
        if (q != Quality.Small && from != to && CanCopy(to, info) && from is not "gif" and not "apng")
        {
            try
            {
                string[] map = to == "mkv" ? ["-map", "0"] : ["-map", "0:v:0", "-map", "0:a?"];
                await Ffmpeg(s, info, ["-i", s.Input, .. map, "-c", "copy", .. Container(to), outFile]);
                if (File.Exists(outFile) && new FileInfo(outFile).Length > 0) return [outFile];
            }
            catch (ConvertException) { }
            try { File.Delete(outFile); } catch { }
        }
        await Ffmpeg(s, info, ["-i", s.Input, "-map", "0:v:0", "-map", "0:a?", .. VideoArgs(to, q, enc, info, still: false), .. Container(to), outFile]);
        return [outFile];
    }

    static bool CanCopy(string to, Probe p) => to switch
    {
        "mkv" => p.Video != null,
        "mp4" or "m4v" or "mov" => p.Video is "h264" or "hevc" or "av1" or "mpeg4" && p.Audio is null or "aac" or "mp3" or "alac" or "ac3" or "eac3" or "opus",
        "webm" => p.Video is "vp8" or "vp9" or "av1" && p.Audio is null or "opus" or "vorbis",
        "ts" => p.Video is "h264" or "hevc" or "mpeg2video" && p.Audio is null or "aac" or "mp3" or "ac3",
        "flv" => p.Video == "h264" && p.Audio is null or "aac" or "mp3",
        _ => false,
    };

    static string[] Container(string to) => to switch
    {
        "mp4" or "m4v" or "mov" => ["-movflags", "+faststart"],
        _ => [],
    };

    static string[] VideoArgs(string to, Quality q, HashSet<string> enc, Probe info, bool still)
    {
        // even sizes (H.264 needs them); the "smaller" setting also caps the size at 1080p
        string limit = q == Quality.Small && Math.Max(info.Width, info.Height) > 1920
            ? "scale='if(gte(iw,ih),min(1920,iw),-2)':'if(gte(iw,ih),-2,min(1920,ih))',scale=trunc(iw/2)*2:trunc(ih/2)*2"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        string crf264 = q switch { Quality.Best => "18", Quality.Balanced => "22", _ => "27" };
        string preset = q == Quality.Best ? "slow" : "medium";
        string aacRate = q switch { Quality.Best => "256k", Quality.Balanced => "192k", _ => "128k" };
        string[] h264 = ["-vf", limit, "-c:v", "libx264", "-preset", preset, "-crf", crf264, "-pix_fmt", "yuv420p"];
        string[] aac = ["-c:a", "aac", "-b:a", aacRate];
        return to switch
        {
            "mp4" or "m4v" or "mov" when q == Quality.Small && enc.Contains("libx265") =>
                ["-vf", limit, "-c:v", "libx265", "-preset", "medium", "-crf", "28", "-pix_fmt", "yuv420p", "-tag:v", "hvc1", .. aac],
            "mp4" or "m4v" or "mov" or "mkv" or "flv" or "ts" => [.. h264, .. aac],
            "webm" => ["-vf", limit, "-c:v", "libvpx-vp9", "-crf", q switch { Quality.Best => "28", Quality.Balanced => "33", _ => "38" }, "-b:v", "0", "-row-mt", "1",
                "-deadline", "good", "-cpu-used", q == Quality.Best ? "2" : "4", "-c:a", "libopus", "-b:a", q == Quality.Small ? "96k" : "160k"],
            "avi" => ["-vf", limit, "-c:v", "mpeg4", "-vtag", "xvid", "-q:v", q switch { Quality.Best => "2", Quality.Balanced => "4", _ => "7" }, "-c:a", "libmp3lame", "-q:a", "2"],
            "wmv" => ["-vf", limit, "-c:v", "wmv2", "-b:v", q switch { Quality.Best => "8M", Quality.Balanced => "4M", _ => "1500k" }, "-c:a", "wmav2", "-b:a", "192k"],
            "mpg" => ["-vf", limit, "-c:v", "mpeg2video", "-q:v", q switch { Quality.Best => "2", Quality.Balanced => "4", _ => "7" }, "-c:a", "mp2", "-b:a", "224k"],
            "3gp" => ["-vf", "scale='min(640,iw)':-2,scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-profile:v", "baseline", "-level", "3.0", "-crf", "28", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ac", "1", "-ar", "44100", "-b:a", "64k"],
            "ogv" => ["-vf", limit, "-c:v", "libtheora", "-q:v", q switch { Quality.Best => "8", Quality.Balanced => "6", _ => "4" }, "-c:a", "libvorbis", "-q:a", "5"],
            _ => [.. h264, .. aac],
        };
    }

    static string[] AudioArgs(string to, Quality q, Probe info, HashSet<string> enc) => to switch
    {
        "mp3" => ["-c:a", "libmp3lame", "-q:a", q switch { Quality.Best => "0", Quality.Balanced => "2", _ => "5" }],
        "wav" => ["-c:a", q == Quality.Best ? "pcm_s24le" : "pcm_s16le"],
        "flac" => ["-c:a", "flac", "-compression_level", q == Quality.Small ? "12" : "8"],
        "m4a" or "m4r" => ["-c:a", "aac", "-b:a", q switch { Quality.Best => "256k", Quality.Balanced => "192k", _ => "128k" }, .. to == "m4r" ? new[] { "-f", "ipod" } : []],
        "aac" => ["-c:a", "aac", "-b:a", q switch { Quality.Best => "256k", Quality.Balanced => "192k", _ => "128k" }, "-f", "adts"],
        "ogg" => ["-c:a", "libvorbis", "-q:a", q switch { Quality.Best => "8", Quality.Balanced => "6", _ => "3" }],
        "opus" => ["-c:a", "libopus", "-b:a", q switch { Quality.Best => "192k", Quality.Balanced => "128k", _ => "80k" }],
        "wma" => ["-c:a", "wmav2", "-b:a", q switch { Quality.Best => "192k", Quality.Balanced => "160k", _ => "96k" }],
        "aiff" => ["-c:a", q == Quality.Best ? "pcm_s24be" : "pcm_s16be"],
        "au" => ["-c:a", "pcm_s16be"],
        "ac3" => ["-c:a", "ac3", "-b:a", q switch { Quality.Best => "448k", Quality.Balanced => "384k", _ => "256k" }],
        "wv" => ["-c:a", "wavpack"],
        "amr" => ["-c:a", "libopencore_amrnb", "-ar", "8000", "-ac", "1", "-b:a", "12.2k"],
        "mka" => info.Audio != null ? ["-c:a", "copy"] : ["-c:a", "flac"],
        _ => [],
    };

    static string SubCodec(string to) => to switch { "vtt" => "webvtt", "ass" => "ass", "ttml" => "ttml", "lrc" => "text", _ => "srt" };

    /// <summary>A soft dark stage with the song's waveform drawn in SweaxMorph's audio colours.</summary>
    static string Waveform() =>
        "color=c=0x14121C:s=1280x720:r=30[bg];" +
        "[0:a]showwaves=s=1280x360:mode=cline:rate=30:scale=sqrt:colors=0x2EC4B6|0x8B7CFF[w];" +
        "[bg][w]overlay=0:180:shortest=1,format=yuv420p[v]";

    /// <summary>Clips one after another: audio joined, videos fitted to the first one's frame.</summary>
    async Task<string> Concat(Step s)
    {
        var infos = new List<Probe>();
        foreach (var f in s.Inputs) infos.Add(await Inspect(f, s.Cancel));
        var total = new Probe(infos.Sum(i => i.Duration), null, null, 0, 0, 0, true);
        var args = new List<string>();
        foreach (var f in s.Inputs) { args.Add("-i"); args.Add(f); }
        string outFile = s.Out();
        int n = s.Inputs.Length;
        var graph = new System.Text.StringBuilder();
        var enc = Encoders();
        if (s.To.Kind == Kind.Audio)
        {
            for (int i = 0; i < n; i++) graph.Append($"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            for (int i = 0; i < n; i++) graph.Append($"[a{i}]");
            graph.Append($"concat=n={n}:v=0:a=1[a]");
            args.AddRange(["-filter_complex", graph.ToString(), "-map", "[a]", .. AudioArgs(s.To.Id, s.Quality, total, enc)]);
        }
        else
        {
            var first = infos.First(i => i.Width > 0);
            int w = first.Width / 2 * 2, h = first.Height / 2 * 2;
            // clips without sound get silence of their own length, so the timeline stays in sync
            int extra = n;
            var audioLabels = new string[n];
            for (int i = 0; i < n; i++)
            {
                if (infos[i].Audio != null) { audioLabels[i] = $"{i}:a:0"; continue; }
                args.AddRange(["-f", "lavfi", "-t", Math.Max(0.1, infos[i].Duration).ToString("0.###", CultureInfo.InvariantCulture), "-i", "anullsrc=r=48000:cl=stereo"]);
                audioLabels[i] = $"{extra++}:a:0";
            }
            for (int i = 0; i < n; i++)
                graph.Append($"[{i}:v:0]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];")
                     .Append($"[{audioLabels[i]}]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            for (int i = 0; i < n; i++) graph.Append($"[v{i}][a{i}]");
            graph.Append($"concat=n={n}:v=1:a=1[v][a]");
            args.AddRange(["-filter_complex", graph.ToString(), "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-preset", "medium", "-crf", "21", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]);
        }
        args.Add(outFile);
        await Ffmpeg(s, total, args.ToArray());
        return outFile;
    }

    static readonly Regex DurationLine = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");

    /// <summary>Runs FFmpeg with live progress; a failure shows FFmpeg's own last words.</summary>
    static async Task Ffmpeg(Step s, Probe info, string[] args)
    {
        double duration = info.Duration;
        var r = await Proc.Run(Engines.FFmpeg.Path!, ["-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats", .. args], s.Cancel,
            onErr: line =>
            {
                if (duration <= 0 && DurationLine.Match(line) is { Success: true } m)
                    duration = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            },
            onOut: line =>
            {
                if (duration > 0 && line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out long us))
                    s.Progress.Report(Math.Clamp(us / 1e6 / duration, 0, 0.99));
            });
        if (r.Code != 0)
        {
            var lines = r.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string why = lines.LastOrDefault(l => !l.StartsWith("Conversion failed")) ?? "FFmpeg";
            throw new ConvertException(Loc.T("Err.Engine", "FFmpeg", why.Length > 180 ? why[..180] : why));
        }
        s.Progress.Report(1);
    }
}
