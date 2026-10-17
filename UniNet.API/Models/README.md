# U2NETP foreground model

- Authors: Xuebin Qin, Zichen Zhang, Chenyang Huang, Masood Dehghan, Osmar R. Zaiane, Martin Jagersand.
- Source/model architecture: https://github.com/xuebinqin/U-2-Net (Apache-2.0; full license in U2NET-LICENSE.txt).
- Unmodified ONNX export: https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx
- Upstream rembg checksum: MD5 `8e83ca70e441ab06c318d82300c84806`.
- SHA-256: `309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8` (verified at runtime).
- Size: 4,574,861 bytes. Package with the application; no runtime model downloads.
- Input: RGB [1,3,320,320], max-channel normalization then ImageNet mean/std. First output is the foreground probability map. See https://github.com/danielgatis/rembg/blob/main/rembg/sessions/u2netp.py and `base.py`.

Used for local foreground segmentation only when the primary portrait provider fails. Does not generate image content. Complex backgrounds/multiple subjects can reduce quality; the existing preview remains mandatory before confirmation.
