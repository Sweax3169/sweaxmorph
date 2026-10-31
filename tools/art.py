# Generates SweaxMorph's README artwork as animated SVGs (light + dark, en + tr).
# usage: python tools/art.py docs/art
# Nunito is subset to the characters used and embedded, so the art looks the same everywhere.
import base64, io, math, os, sys
from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(ROOT, 'src', 'SweaxMorph', 'Assets', 'Fonts')

T = {
    'light': dict(bg1='#FFF7FB', bg2='#F1EBFF', ink='#2B2140', ink2='#6F6585', ink3='#A49BB6', surface='#FFFFFF', line='#EAE3F2', shadow='.16', glow='.55'),
    'dark': dict(bg1='#1E1830', bg2='#15111F', ink='#F6F0FF', ink2='#B8ADCC', ink3='#7D7392', surface='#272033', line='#3A3147', shadow='.45', glow='.22'),
}
HUE = dict(Image='#FF6B9A', Video='#8B7CFF', Audio='#2EC4B6', Document='#4C8DFF', Sheet='#3CCB7F', Slides='#FF8A4C', Pdf='#FF5A5F',
           Ebook='#E879F9', Data='#A3C93A', Archive='#C08B5C', Subtitle='#8E9AAF', Font='#F4B740')

L = {
    'en': dict(kicker='File converter for Windows 10 and 11', tag1='Drop any file on Morph.', tag2='Pick a format. Done.',
               facts='126 formats  ·  229 extensions  ·  offline', keys='Win + Alt + F  ·  Space while dragging  ·  right-click',
               f_shrink=('Shrink', 'Same format, much smaller.'), f_combine=('Combine', 'Many photos, one PDF.'),
               f_route=('Routes', 'No direct way? Morph plans one.'), f_safe=('Never overwrites', 'Results land next to the original.'),
               f_ocr=('Reads scans', 'Pictures and scanned PDFs become text.'), f_fast=('Fast when it can be', 'MP4 → MKV copies streams in a second.'),
               plan='How a route is planned', plan_sub='Every converter offers what it can do. The cheapest chain wins.', built='built in', once='downloaded once', yours='if installed'),
    'tr': dict(kicker='Windows 10 ve 11 için dosya dönüştürücü', tag1='Dosyayı Morph’a bırak.', tag2='Formatı seç. Bitti.',
               facts='126 format  ·  229 uzantı  ·  çevrimdışı', keys='Win + Alt + F  ·  sürüklerken Boşluk  ·  sağ tık',
               f_shrink=('Küçült', 'Aynı format, çok daha küçük.'), f_combine=('Birleştir', 'Bir sürü fotoğraf, tek PDF.'),
               f_route=('Yol planı', 'Doğrudan yol yoksa Morph bir tane bulur.'), f_safe=('Asla üzerine yazmaz', 'Sonuç asıl dosyanın yanına gelir.'),
               f_ocr=('Taramaları okur', 'Görseller ve taranmış PDF’ler metne dönüşür.'), f_fast=('Olabildiğince hızlı', 'MP4 → MKV akışları bir saniyede kopyalar.'),
               plan='Bir yol nasıl planlanır', plan_sub='Her dönüştürücü neler yapabildiğini söyler. En ucuz zincir kazanır.', built='içinde', once='bir kez iner', yours='kuruluysa'),
}


def font_face(weights_and_files, text):
    css = ''
    chars = ''.join(sorted(set(text + 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;·+→’()%-ÇĞİÖŞÜçğıöşü!?')))
    for weight, file in weights_and_files:
        f = TTFont(os.path.join(FONTS, file))
        opts = subset.Options()
        opts.flavor = 'woff2'
        opts.layout_features = ['kern', 'liga']
        sub = subset.Subsetter(options=opts)
        sub.populate(text=chars)
        sub.subset(f)
        buf = io.BytesIO()
        f.flavor = 'woff2'
        f.save(buf)
        b64 = base64.b64encode(buf.getvalue()).decode()
        css += f"@font-face{{font-family:N;font-weight:{weight};src:url(data:font/woff2;base64,{b64}) format('woff2')}}"
    return css


def blob_path(cx, cy, r, n=18, wob=None):
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n - math.pi / 2
        rr = r * (1 + (wob[i] if wob else 0))
        rr *= 1 - 0.07 * max(0, math.sin(a)) ** 4
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    d = f'M{pts[0][0]:.1f},{pts[0][1]:.1f}'
    for i in range(n):
        p0, p1, p2, p3 = pts[(i - 1) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        d += f' C{c1[0]:.1f},{c1[1]:.1f} {c2[0]:.1f},{c2[1]:.1f} {p2[0]:.1f},{p2[1]:.1f}'
    return d + 'Z'


def morph(cx, cy, r, top='#CDB8FF', bottom='#FF8FC1', uid='m', happy=False):
    """Morph as SVG: gel body, gloss, kawaii eyes that blink and glance around, cheeks, smile."""
    body = blob_path(cx, cy, r)
    ex, ey, rx, ry = r * 0.31, -r * 0.08, r * 0.092, r * 0.118
    eyes = ''
    for side in (-1, 1):
        x, y = cx + side * ex, cy + ey
        if happy:
            eyes += f'<path d="M{x - rx * 1.15:.1f},{y + ry * .25:.1f} Q{x:.1f},{y - ry * 1.35:.1f} {x + rx * 1.15:.1f},{y + ry * .25:.1f}" stroke="#2B2140" stroke-width="{rx * .62:.1f}" stroke-linecap="round" fill="none"/>'
        else:
            eyes += (f'<g class="eye" style="transform-origin:{x:.1f}px {y:.1f}px"><ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="#2B2140"/>'
                     f'<circle cx="{x + rx * .34:.1f}" cy="{y - ry * .36:.1f}" r="{rx * .38:.1f}" fill="#fff"/><circle cx="{x - rx * .32:.1f}" cy="{y + ry * .38:.1f}" r="{rx * .17:.1f}" fill="#fff"/></g>')
    mx, my, w = cx, cy + r * 0.16, r * 0.22
    mouth = (f'<path d="M{mx - w * 1.4 / 2:.1f},{my - r * .03:.1f} Q{mx:.1f},{my + r * .22:.1f} {mx + w * 1.4 / 2:.1f},{my - r * .03:.1f} Z" fill="#2B2140"/>'
             if happy else f'<path d="M{mx - w / 2:.1f},{my - r * .018:.1f} Q{mx:.1f},{my + r * .066:.1f} {mx + w / 2:.1f},{my - r * .018:.1f}" stroke="#2B2140" stroke-width="{r * .045:.1f}" stroke-linecap="round" fill="none"/>')
    return f'''<defs>
  <linearGradient id="{uid}b" x1=".2" y1="0" x2=".8" y2="1"><stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/></linearGradient>
  <radialGradient id="{uid}r" cx=".45" cy=".42" r=".62" fx=".42" fy=".36"><stop offset=".62" stop-color="#2A1446" stop-opacity="0"/><stop offset="1" stop-color="#2A1446" stop-opacity=".25"/></radialGradient>
  <radialGradient id="{uid}g" cx=".34" cy=".28" r=".42"><stop offset="0" stop-color="#fff" stop-opacity=".5"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
  <radialGradient id="{uid}s"><stop offset="0" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
  <radialGradient id="{uid}c"><stop offset="0" stop-color="#FF6EA0" stop-opacity=".6"/><stop offset="1" stop-color="#FF6EA0" stop-opacity="0"/></radialGradient>
  <radialGradient id="{uid}f"><stop offset="0" stop-color="#140A28" stop-opacity=".28"/><stop offset="1" stop-color="#140A28" stop-opacity="0"/></radialGradient>
</defs>
<ellipse cx="{cx}" cy="{cy + r * 1.02:.1f}" rx="{r * .95:.1f}" ry="{r * .17:.1f}" fill="url(#{uid}f)" class="shadow" style="transform-origin:{cx}px {cy + r:.1f}px"/>
<g class="breathe" style="transform-origin:{cx}px {cy + r:.1f}px">
  <path d="{body}" fill="url(#{uid}b)"/><path d="{body}" fill="url(#{uid}r)"/><path d="{body}" fill="url(#{uid}g)"/>
  <ellipse cx="{cx - r * .42:.1f}" cy="{cy - r * .5:.1f}" rx="{r * .27:.1f}" ry="{r * .13:.1f}" fill="url(#{uid}s)" transform="rotate(-28 {cx - r * .42:.1f} {cy - r * .5:.1f})"/>
  <ellipse cx="{cx - r * .14:.1f}" cy="{cy - r * .72:.1f}" rx="{r * .045:.1f}" ry="{r * .04:.1f}" fill="#fff" opacity=".8"/>
  <g class="look">{eyes}</g>
  <ellipse cx="{cx - r * .5:.1f}" cy="{cy + r * .1:.1f}" rx="{r * .16:.1f}" ry="{r * .1:.1f}" fill="url(#{uid}c)"/><ellipse cx="{cx + r * .5:.1f}" cy="{cy + r * .1:.1f}" rx="{r * .16:.1f}" ry="{r * .1:.1f}" fill="url(#{uid}c)"/>
  {mouth}
</g>'''


def petal(x, y, label, hue, i, size=30, cls='petal'):
    fs = 13 if len(label) <= 4 else 11
    return (f'<g class="{cls}" style="animation-delay:{-i * .37:.2f}s"><circle cx="{x:.1f}" cy="{y + 5:.1f}" r="{size + 2}" fill="{hue}" opacity=".18"/>'
            f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{size}" fill="url(#p{hue[1:]})"/>'
            f'<ellipse cx="{x - size * .38:.1f}" cy="{y - size * .46:.1f}" rx="{size * .3:.1f}" ry="{size * .15:.1f}" fill="#fff" opacity=".6" transform="rotate(-30 {x - size * .38:.1f} {y - size * .46:.1f})"/>'
            f'<text x="{x:.1f}" y="{y + fs * .36:.1f}" font-size="{fs}" font-weight="900" fill="#fff" text-anchor="middle">{label}</text></g>')


def petal_defs(hues):
    out = ''
    for h in sorted(set(hues)):
        r, g, b = int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)
        light = '#%02X%02X%02X' % tuple(int(v + (255 - v) * .38) for v in (r, g, b))
        out += f'<linearGradient id="p{h[1:]}" x1=".25" y1="0" x2=".75" y2="1"><stop offset="0" stop-color="{light}"/><stop offset="1" stop-color="{h}"/></linearGradient>'
    return f'<defs>{out}</defs>'


BASE_CSS = '''
text{font-family:N,'Segoe UI',sans-serif}
.breathe{animation:breathe 3.4s ease-in-out infinite}
@keyframes breathe{0%,100%{transform:scale(1,1)}50%{transform:scale(1.025,.975)}}
.shadow{animation:shadow 3.4s ease-in-out infinite}
@keyframes shadow{50%{transform:scale(1.04,1)}}
.eye{animation:blink 4.6s infinite}
@keyframes blink{0%,46%,50%,100%{transform:scaleY(1)}48%{transform:scaleY(.08)}}
.look{animation:look 7s ease-in-out infinite}
@keyframes look{0%,18%{transform:translate(0,0)}26%,44%{transform:translate(5px,-2px)}52%,70%{transform:translate(-5px,1px)}78%,100%{transform:translate(0,0)}}
.petal{animation:bob 3.2s ease-in-out infinite}
@keyframes bob{50%{transform:translateY(-5px)}}
'''


def banner(theme, lang):
    c, l = T[theme], L[lang]
    W, H = 1280, 520
    cx, cy, r = 940, 252, 92
    labels = [('PNG', 'Image'), ('MP4', 'Video'), ('MP3', 'Audio'), ('PDF', 'Pdf'), ('DOCX', 'Document'), ('XLSX', 'Sheet'), ('EPUB', 'Ebook'), ('ZIP', 'Archive'), ('JSON', 'Data'), ('WOFF2', 'Font')]
    ring = 178
    petals = ''.join(petal(cx + math.cos(-math.pi / 2 + 2 * math.pi * i / len(labels)) * ring, cy + 6 + math.sin(-math.pi / 2 + 2 * math.pi * i / len(labels)) * ring, lab, HUE[k], i)
                     for i, (lab, k) in enumerate(labels))
    text = l['kicker'] + l['tag1'] + l['tag2'] + l['facts'] + l['keys'] + 'SweaxMorph' + ''.join(x for x, _ in labels)
    css = font_face([(800, 'Nunito-ExtraBold.ttf'), (900, 'Nunito-Black.ttf')], text) + BASE_CSS
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">
<style>{css}</style>
<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{c['bg1']}"/><stop offset="1" stop-color="{c['bg2']}"/></linearGradient>
<radialGradient id="g1"><stop offset="0" stop-color="#CDB8FF" stop-opacity="{c['glow']}"/><stop offset="1" stop-color="#CDB8FF" stop-opacity="0"/></radialGradient>
<radialGradient id="g2"><stop offset="0" stop-color="#FF8FC1" stop-opacity="{c['glow']}"/><stop offset="1" stop-color="#FF8FC1" stop-opacity="0"/></radialGradient>
<linearGradient id="word" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#8B7CFF"/><stop offset=".55" stop-color="#FF6B9A"/><stop offset="1" stop-color="#FF9A62"/></linearGradient>
<clipPath id="card"><rect width="{W}" height="{H}" rx="28"/></clipPath></defs>
{petal_defs([HUE[k] for _, k in labels])}
<g clip-path="url(#card)">
<rect width="{W}" height="{H}" fill="url(#bg)"/>
<circle cx="140" cy="60" r="360" fill="url(#g1)"/><circle cx="1120" cy="470" r="380" fill="url(#g2)"/>
<text x="84" y="150" font-size="18" font-weight="800" fill="{c['ink2']}">{l['kicker']}</text>
<text x="80" y="226" font-size="74" font-weight="900" fill="url(#word)">SweaxMorph</text>
<text x="84" y="290" font-size="30" font-weight="900" fill="{c['ink']}">{l['tag1']}</text>
<text x="84" y="332" font-size="30" font-weight="900" fill="{c['ink']}">{l['tag2']}</text>
<text x="84" y="392" font-size="17" font-weight="800" fill="{c['ink2']}">{l['facts']}</text>
<text x="84" y="424" font-size="17" font-weight="800" fill="{c['ink3']}">{l['keys']}</text>
{morph(cx, cy, r, uid='bm')}
{petals}
</g>
</svg>'''


def card(theme, lang, key, scene, W=600, H=300):
    c, l = T[theme], L[lang]
    title, sub = l[key]
    css = font_face([(800, 'Nunito-ExtraBold.ttf'), (900, 'Nunito-Black.ttf')], title + sub + 'PNGJPGPDFMP4MKVDOCXPPTXname(2).jpgphoto.jpg12 MB1.4 MBHelloMerhabaDünya') + BASE_CSS + scene_css
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">
<style>{css}</style>
<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{c['bg1']}"/><stop offset="1" stop-color="{c['bg2']}"/></linearGradient><clipPath id="card"><rect width="{W}" height="{H}" rx="24"/></clipPath></defs>
<g clip-path="url(#card)"><rect width="{W}" height="{H}" fill="url(#bg)"/>
{scene(c, l)}
<text x="34" y="{H - 62}" font-size="27" font-weight="900" fill="{c['ink']}">{title}</text>
<text x="34" y="{H - 30}" font-size="16.5" font-weight="800" fill="{c['ink2']}">{sub}</text>
</g></svg>'''


scene_css = '''
.squeeze{animation:squeeze 3.6s cubic-bezier(.34,1.56,.64,1) infinite;transform-box:fill-box;transform-origin:center}
@keyframes squeeze{0%,30%{transform:scale(1)}50%,85%{transform:scale(.55)}100%{transform:scale(1)}}
.size1{animation:fadeout 3.6s infinite}.size2{animation:fadein 3.6s infinite}
@keyframes fadeout{0%,35%{opacity:1}45%,90%{opacity:0}100%{opacity:1}}
@keyframes fadein{0%,40%{opacity:0}50%,88%{opacity:1}96%,100%{opacity:0}}
.fly1{animation:fly1 3.6s ease-in-out infinite}.fly2{animation:fly2 3.6s ease-in-out infinite}.fly3{animation:fly3 3.6s ease-in-out infinite}
@keyframes fly1{0%,15%{transform:translate(0,0);opacity:1}45%{transform:translate(190px,10px);opacity:0}100%{opacity:0}}
@keyframes fly2{0%,22%{transform:translate(0,0);opacity:1}52%{transform:translate(120px,-6px);opacity:0}100%{opacity:0}}
@keyframes fly3{0%,29%{transform:translate(0,0);opacity:1}59%{transform:translate(50px,-22px);opacity:0}100%{opacity:0}}
.pdf{animation:pdf 3.6s cubic-bezier(.34,1.56,.64,1) infinite;transform-box:fill-box;transform-origin:center}
@keyframes pdf{0%,50%{transform:scale(.9)}62%{transform:scale(1.12)}72%,100%{transform:scale(1)}}
.node2{animation:light 3.2s infinite}
@keyframes light{0%,40%{opacity:.35}50%,100%{opacity:1}}
.node3{animation:light3 3.2s infinite}
@keyframes light3{0%,85%{opacity:.35}95%,100%{opacity:1}}
.newfile{animation:newfile 3.4s cubic-bezier(.34,1.56,.64,1) infinite;transform-box:fill-box;transform-origin:center}
@keyframes newfile{0%,25%{transform:scale(0)}40%,90%{transform:scale(1)}100%{transform:scale(0)}}
.scan{animation:scan 2.6s ease-in-out infinite}
@keyframes scan{0%{transform:translateY(0)}50%{transform:translateY(96px)}100%{transform:translateY(0)}}
.typed{animation:typed 2.6s steps(14) infinite}
@keyframes typed{0%{clip-path:inset(0 100% 0 0)}60%,100%{clip-path:inset(0 0 0 0)}}
.stream{animation:stream 1.4s linear infinite}
@keyframes stream{to{stroke-dashoffset:-40}}
'''


def tile(x, y, w, h, hue, label, cls=''):
    return (f'<g class="{cls}"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="{hue}"/>'
            f'<rect x="{x}" y="{y}" width="{w}" height="{h * .45:.0f}" rx="12" fill="#fff" opacity=".18"/>'
            f'<text x="{x + w / 2}" y="{y + h / 2 + 6}" font-size="16" font-weight="900" fill="#fff" text-anchor="middle">{label}</text></g>')


def s_shrink(c, l):
    return (f'<g class="squeeze">{tile(240, 40, 120, 120, HUE["Image"], "photo.jpg")}</g>'
            f'<text class="size1" x="300" y="190" font-size="17" font-weight="900" fill="{c["ink2"]}" text-anchor="middle">12 MB</text>'
            f'<text class="size2" x="300" y="190" font-size="17" font-weight="900" fill="#2EC4B6" text-anchor="middle">1.4 MB</text>')


def s_combine(c, l):
    return (f'<g class="fly1">{tile(60, 50, 74, 56, "#FF9A62", "JPG")}</g><g class="fly2">{tile(130, 80, 74, 56, HUE["Image"], "PNG")}</g>'
            f'<g class="fly3">{tile(200, 46, 74, 56, "#B39CFF", "JPG")}</g>'
            f'<g class="pdf">{tile(380, 34, 104, 132, HUE["Pdf"], "PDF")}</g>')


def s_route(c, l):
    out = ''
    for i, (x, lab, hue) in enumerate([(108, 'PPTX', HUE['Slides']), (300, 'PDF', HUE['Pdf']), (492, 'PNG', HUE['Image'])]):
        cls = '' if i == 0 else f'node{i + 1}'
        out += f'<g class="{cls}"><circle cx="{x}" cy="96" r="44" fill="{hue}"/><circle cx="{x - 14}" cy="78" r="14" fill="#fff" opacity=".35"/><text x="{x}" y="102" font-size="17" font-weight="900" fill="#fff" text-anchor="middle">{lab}</text></g>'
    return (f'<path d="M152,96 L256,96 M344,96 L448,96" stroke="{c["line"]}" stroke-width="6" stroke-linecap="round"/>' +
            '<circle r="9" fill="#8B7CFF"><animateMotion dur="3.2s" repeatCount="indefinite" path="M108,96 L492,96"/></circle>' + out)


def s_safe(c, l):
    return (tile(150, 54, 130, 92, HUE['Image'], 'photo.jpg') +
            f'<g class="newfile">{tile(320, 54, 130, 92, "#2EC4B6", "photo (2).jpg")}</g>')


def s_ocr(c, l):
    word = 'Merhaba Dünya' if l is L['tr'] else 'Hello, world'
    return (f'<rect x="150" y="30" width="300" height="140" rx="16" fill="{c["surface"]}" stroke="{c["line"]}" stroke-width="2"/>'
            f'<rect x="176" y="58" width="200" height="12" rx="6" fill="{c["ink3"]}" opacity=".5"/><rect x="176" y="82" width="240" height="12" rx="6" fill="{c["ink3"]}" opacity=".5"/>'
            f'<rect x="176" y="106" width="160" height="12" rx="6" fill="{c["ink3"]}" opacity=".5"/>'
            f'<rect class="scan" x="150" y="40" width="300" height="4" fill="#2EC4B6" opacity=".8"/>'
            f'<text class="typed" x="176" y="150" font-size="17" font-weight="900" fill="#2EC4B6">{word}</text>')


def s_fast(c, l):
    return (tile(90, 60, 120, 80, HUE['Video'], 'MP4') + tile(390, 60, 120, 80, HUE['Video'], 'MKV') +
            f'<path class="stream" d="M220,88 L380,88" stroke="#8B7CFF" stroke-width="5" stroke-dasharray="14 6" stroke-linecap="round"/>'
            f'<path class="stream" d="M220,112 L380,112" stroke="#2EC4B6" stroke-width="5" stroke-dasharray="14 6" stroke-linecap="round"/>')


CARDS = [('shrink', 'f_shrink', s_shrink), ('combine', 'f_combine', s_combine), ('route', 'f_route', s_route),
         ('safe', 'f_safe', s_safe), ('ocr', 'f_ocr', s_ocr), ('fast', 'f_fast', s_fast)]


def main(out):
    os.makedirs(out, exist_ok=True)
    for theme in T:
        for lang in L:
            open(os.path.join(out, f'banner-{theme}-{lang}.svg'), 'w', encoding='utf-8').write(banner(theme, lang))
            for name, key, scene in CARDS:
                open(os.path.join(out, f'card-{name}-{theme}-{lang}.svg'), 'w', encoding='utf-8').write(card(theme, lang, key, scene))
    print('written to', out)


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'docs', 'art'))
