$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$assets=Join-Path $root 'src/Afmc.Find.App/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Copy-Item -LiteralPath 'C:\Users\CHENKEYOU\AppData\Local\ChatGPT-MultiInstance\profiles\PRO\codex\generated_images\01a1a491-fdf4-7021-a17a-d54da7c92934\exec-e7c43337-96da-4c25-961b-f1a48369d58a.png' -Destination (Join-Path $assets 'afmc-find.png')
$source=[System.Drawing.Bitmap]::FromFile((Join-Path $assets 'afmc-find.png'))
if($source.GetPixel(0,0).A -ne 0){throw 'Expected transparent icon background'}
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
foreach($size in $sizes){
 $bitmap=New-Object System.Drawing.Bitmap($size,$size)
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $graphics.DrawImage($source,0,0,$size,$size)
 $stream=New-Object System.IO.MemoryStream
 $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
 $frames+= ,$stream.ToArray()
 $graphics.Dispose();$bitmap.Dispose();$stream.Dispose()
}
$source.Dispose()
$file=[System.IO.File]::Create((Join-Path $assets 'afmc-find.ico'))
$writer=New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
$offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){
 $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
 $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
 $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
 $offset+=$frames[$i].Length
}
foreach($frame in $frames){$writer.Write([byte[]]$frame)}
$writer.Dispose();$file.Dispose()
'PASS transparent PNG and ICO: 16/20/24/32/40/48/64/128/256'
