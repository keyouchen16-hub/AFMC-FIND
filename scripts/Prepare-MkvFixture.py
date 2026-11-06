import pathlib, subprocess, imageio_ffmpeg
root = pathlib.Path(__file__).resolve().parent.parent
target = root / '.tools/native-mkv-fixture'
target.mkdir(exist_ok=True)
subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y',
    '-i', str(root / '.tools/player-installed-fixture/AFMC-original.avi'),
    '-map', '0', '-c', 'copy', '-allow_raw_vfw', '1', str(target / 'original.mkv')],
    check=True, creationflags=subprocess.CREATE_NO_WINDOW)
print('Original generated MKV fixture bytes:', (target / 'original.mkv').stat().st_size)
