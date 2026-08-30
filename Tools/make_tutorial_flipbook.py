"""대기방 전시물 안내 영상(mp4)을 플립북 아틀라스 PNG로 굽는다.

Unity 의 VideoPlayer 는 이 프로젝트 환경에서 첫 프레임을 내놓기까지 몇 초씩 걸리거나 아예 멈춰서,
안내 영상을 텍스처로 미리 구워 쓰기로 했다. 여기서 만든 PNG 를 Unity 에서
DXT1 / mipmap 끔 / npotScale None 으로 임포트하고, 출력된 격자와 프레임 수를
TutorialFlipbook 에셋에 넣으면 된다.

한 장에 다 담지 못하면 여러 장으로 나눈다. 텍스처 한 변이 8192 를 넘으면 Unity 가 축소해
격자가 통째로 어긋나기 때문이다. 나눌 때는 장마다 행 수가 같도록 고르게 쪼갠다 —
칸 계산이 장마다 달라지지 않아야 재생 쪽이 단순해진다.

사용:
    python Tools/make_tutorial_flipbook.py                  # 현재 설정 그대로 다시 굽기
    python Tools/make_tutorial_flipbook.py --fps 20         # 더 부드럽게 (용량 증가)
    python Tools/make_tutorial_flipbook.py --width 640 --height 360
"""

import argparse
import glob
import math
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

# 텍스처 한 변의 한도. Unity 임포터의 Max Size 와 맞춰야 한다.
MAX_ATLAS_SIZE = 8192

SOURCE_DIR = "Assets/Imported/Video"
OUTPUT_DIR = "Assets/Imported/Video/Flipbook"
CLIPS = ["Battery", "CCTV", "Clue", "Capturetool"]


def find_ffmpeg():
    """pip 로 설치한 imageio-ffmpeg 의 번들 바이너리를 먼저 쓰고, 없으면 PATH 를 본다."""
    try:
        import imageio_ffmpeg

        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        path = shutil.which("ffmpeg")
        if path:
            return path
        sys.exit("ffmpeg 가 없습니다. `pip install imageio-ffmpeg` 로 설치하세요.")


def extract_frames(ffmpeg, source, frame_dir, fps, width, height):
    shutil.rmtree(frame_dir, ignore_errors=True)
    os.makedirs(frame_dir, exist_ok=True)

    subprocess.run(
        [ffmpeg, "-y", "-loglevel", "error", "-i", source,
         "-vf", f"fps={fps},scale={width}:{height}", "-pix_fmt", "rgb24",
         os.path.join(frame_dir, "f_%04d.png")],
        check=True,
    )

    return sorted(glob.glob(os.path.join(frame_dir, "f_*.png")))


def plan_grid(frame_count, width, height):
    """(열, 행, 장수)를 정한다. 장마다 같은 격자를 쓰고, 마지막 장에만 빈 칸이 남는다."""
    columns = MAX_ATLAS_SIZE // width
    max_rows = MAX_ATLAS_SIZE // height
    atlas_count = math.ceil(frame_count / (columns * max_rows))
    rows = math.ceil(frame_count / (columns * atlas_count))
    return columns, rows, atlas_count


def bake(ffmpeg, name, fps, width, height):
    source = os.path.join(SOURCE_DIR, name + ".mp4")
    if not os.path.exists(source):
        print(f"  건너뜀 - 원본 없음: {source}")
        return None

    frame_dir = os.path.join(tempfile.gettempdir(), "flipbook_frames", name)
    frames = extract_frames(ffmpeg, source, frame_dir, fps, width, height)
    columns, rows, atlas_count = plan_grid(len(frames), width, height)
    per_atlas = columns * rows

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for old in glob.glob(os.path.join(OUTPUT_DIR, f"{name}_Flipbook*.png")):
        os.remove(old)

    total_bytes = 0
    for atlas_index in range(atlas_count):
        chunk = frames[atlas_index * per_atlas:(atlas_index + 1) * per_atlas]

        # 마지막 장에 남는 빈 칸은 검게 둔다. 재생은 프레임 수까지만 하므로 화면에 나오지 않는다.
        atlas = Image.new("RGB", (columns * width, rows * height), (0, 0, 0))
        for index, frame in enumerate(chunk):
            with Image.open(frame) as image:
                atlas.paste(image, ((index % columns) * width, (index // columns) * height))

        suffix = "" if atlas_count == 1 else f"_{atlas_index}"
        atlas_path = os.path.join(OUTPUT_DIR, f"{name}_Flipbook{suffix}.png")
        atlas.save(atlas_path, optimize=True)
        atlas.close()
        total_bytes += os.path.getsize(atlas_path)

    shutil.rmtree(frame_dir, ignore_errors=True)
    return len(frames), columns, rows, atlas_count, columns * width, rows * height, total_bytes


def main():
    parser = argparse.ArgumentParser(description="안내 영상을 플립북 아틀라스로 굽는다")
    parser.add_argument("--fps", type=int, default=15)
    parser.add_argument("--width", type=int, default=960)
    parser.add_argument("--height", type=int, default=540)
    parser.add_argument("--clips", nargs="*", default=CLIPS)
    args = parser.parse_args()

    if args.width % 4 or args.height % 4:
        sys.exit("가로·세로는 4의 배수여야 합니다. DXT1 압축이 4x4 블록 단위라 그렇습니다.")

    ffmpeg = find_ffmpeg()
    print(f"ffmpeg: {ffmpeg}")
    print(f"설정: {args.width}x{args.height} / {args.fps}fps\n")

    results = []
    for name in args.clips:
        print(f"굽는 중: {name}")
        result = bake(ffmpeg, name, args.fps, args.width, args.height)
        if result:
            results.append((name,) + result)

    print(f"\n{'클립':<14}{'프레임':>7}{'격자':>9}{'장수':>5}{'아틀라스':>14}{'PNG':>9}")
    for name, frames, columns, rows, count, atlas_w, atlas_h, size in results:
        print(f"{name:<14}{frames:>7}{f'{columns}x{rows}':>9}{count:>5}"
              f"{f'{atlas_w}x{atlas_h}':>14}{size / 1048576:>8.1f}M")

    print("\nTutorialFlipbook 에셋에 위 프레임 수와 격자(열/행), fps 를 넣고 아틀라스를 순서대로 물리세요.")


if __name__ == "__main__":
    main()
