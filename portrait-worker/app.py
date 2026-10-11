import os
import secrets
import threading
from functools import lru_cache
from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import Response
from rembg import new_session, remove
from PIL.Image import DecompressionBombError as ImageError
from portrait import MAX_BYTES, decode_source, compose_cutout

app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)
token = os.environ.get('PORTRAIT_WORKER_TOKEN', '')
if len(token) < 32:
    raise RuntimeError('PORTRAIT_WORKER_TOKEN must be at least 32 characters')
gate = threading.BoundedSemaphore(1)


@lru_cache(maxsize=1)
def session():
    return new_session('birefnet-portrait')


@app.post('/remove-background')
async def segment(request: Request):
    if not secrets.compare_digest(request.headers.get('X-Worker-Token', ''), token):
        raise HTTPException(401, 'Unauthorized')
    raw = bytearray()
    async for chunk in request.stream():
        raw.extend(chunk)
        if len(raw) > MAX_BYTES:
            raise HTTPException(413, 'Image too large')
    # Inference runs outside the event loop, so overload can be rejected promptly.
    if not gate.acquire(blocking=False):
        raise HTTPException(429, 'Processing busy')
    try:
        from starlette.concurrency import run_in_threadpool
        def process():
            source = decode_source(bytes(raw))
            mask = remove(source.convert('RGB'), session=session(), only_mask=True)
            return compose_cutout(source, mask)
        result = await run_in_threadpool(process)
        return Response(result, media_type='image/png', headers={'Cache-Control': 'no-store'})
    except (ValueError, OSError, ImageError) as error:
        raise HTTPException(422, 'Invalid image or segmentation') from error
    finally:
        gate.release()
