import io
from PIL import Image, ImageChops, ImageOps

MAX_BYTES = 5 * 1024 * 1024
MAX_PIXELS = 12_000_000
Image.MAX_IMAGE_PIXELS = MAX_PIXELS


def decode_source(raw):
    if not raw or len(raw) > MAX_BYTES:
        raise ValueError('Invalid image size')
    source = Image.open(io.BytesIO(raw))
    if source.format not in ('JPEG', 'PNG', 'WEBP') or getattr(source, 'n_frames', 1) != 1:
        raise ValueError('Unsupported image')
    if source.width * source.height > MAX_PIXELS or max(source.size) > 6000:
        raise ValueError('Image dimensions too large')
    source.load()
    return ImageOps.exif_transpose(source).convert('RGBA')


def compose_cutout(source, mask):
    if mask.size != source.size:
        raise ValueError('Mask dimensions differ')
    alpha = ImageChops.multiply(source.getchannel('A'), mask.convert('L'))
    low, high = alpha.getextrema()
    if low == 255 or high == 0:
        raise ValueError('No usable foreground/background segmentation')
    result = source.copy()
    result.putalpha(alpha)  # Never replace RGB with model output or edge decontamination.
    output = io.BytesIO()
    result.save(output, format='PNG')
    return output.getvalue()
