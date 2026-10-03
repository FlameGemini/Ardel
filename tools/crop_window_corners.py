# -*- coding: utf-8 -*-
"""
Windows 11 窗口截图圆角无损裁剪与外边缘黑边清理工具
用法:
  python tools/crop_window_corners.py <图片路径或图片所在文件夹> [圆角半径(默认10)] [内缩像素(默认2)]

特性:
  1. 4x 超采样亚像素抗锯齿 (Supersampling Anti-Aliasing)，边缘极其平滑圆润；
  2. 自动剔除 4 个圆角外面的桌面背景杂色/黑边，输出干净的透明背景 PNG；
  3. 支持单张图片或整个文件夹一键批量处理。
"""
import os
import sys
from pathlib import Path
from PIL import Image, ImageDraw


def clean_window_corners(img: Image.Image, radius: int = 10, inset: int = 2) -> Image.Image:
    """对图片四角应用精确的内缩抗锯齿圆角遮罩，将圆角外侧像素转为完全透明。"""
    w, h = img.size
    img_rgba = img.convert("RGBA")
    
    scale = 4  # 4x 超采样以实现顶级平滑边缘
    mask_large = Image.new("L", (w * scale, h * scale), 0)
    d = ImageDraw.Draw(mask_large)
    
    # 绘制抗锯齿圆角矩形
    d.rounded_rectangle(
        [
            inset * scale,
            inset * scale,
            (w - inset) * scale - 1,
            (h - inset) * scale - 1
        ],
        radius=radius * scale,
        fill=255
    )
    
    # 降采样回原图尺寸获得平滑 Alpha 蒙版
    mask = mask_large.resize((w, h), Image.Resampling.LANCZOS)
    
    # 组合为透明背景图像
    result = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    result.paste(img_rgba, (0, 0), mask)
    return result


def process_path(target_path: str, radius: int = 10, inset: int = 2) -> None:
    p = Path(target_path).resolve()
    
    if p.is_file():
        files = [p]
        out_dir = p.parent / "cleaned_screenshots"
    elif p.is_dir():
        files = [f for f in p.glob("*") if f.suffix.lower() in [".png", ".jpg", ".jpeg", ".bmp", ".webp"]]
        out_dir = p / "cleaned_screenshots"
    else:
        print(f"错误: 路径不存在 -> {p}")
        return

    if not files:
        print(f"未找到可处理的图片文件: {p}")
        return

    out_dir.mkdir(parents=True, exist_ok=True)
    print(f"开始处理 {len(files)} 张截图 (圆角半径={radius}px, 内缩={inset}px)...")

    for f in files:
        try:
            with Image.open(f) as im:
                cleaned = clean_window_corners(im, radius=radius, inset=inset)
                out_file = out_dir / f"{f.stem}_clean.png"
                cleaned.save(out_file, "PNG")
                print(f"  [OK] {f.name} -> {out_file.name}")
        except Exception as ex:
            print(f"  [FAIL] {f.name}: {ex}")

    print(f"\n全部处理完成！纯净透明圆角截图已保存在:\n-> {out_dir}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        # 如果直接双击运行，默认处理桌面 debug 目录或当前目录
        default_dir = Path(r"C:\Users\torch\OneDrive\Desktop\debug")
        if default_dir.exists():
            process_path(str(default_dir))
    else:
        path_arg = sys.argv[1]
        rad_arg = int(sys.argv[2]) if len(sys.argv) > 2 else 10
        inset_arg = int(sys.argv[3]) if len(sys.argv) > 3 else 2
        process_path(path_arg, rad_arg, inset_arg)
