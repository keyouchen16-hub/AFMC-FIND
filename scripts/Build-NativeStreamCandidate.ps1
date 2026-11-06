param([switch]$BoostOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$buildRoot = Join-Path $root '.tools/native-stream-build'
$python = Join-Path $buildRoot 'python-dev/tools'
$boost = Join-Path $buildRoot 'boost_1_87_0'
$cmake = 'C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$jamPython = $python.Replace('\','/')
"using python : 3.12 : `"$jamPython/python.exe`" : `"$jamPython/include`" : `"$jamPython/libs`" ;" | Set-Content (Join-Path $buildRoot 'user-config.jam') -Encoding utf8
Push-Location $boost
try {
  & .\b2.exe '--with-python' 'toolset=msvc-14.3' 'address-model=64' 'variant=release' 'link=static' 'runtime-link=shared' 'threading=multi' 'python=3.12' '-j4' "--user-config=$buildRoot/user-config.jam" 'stage' *> (Join-Path $buildRoot 'boost-build.log')
  if ($LASTEXITCODE -ne 0) { throw "Boost failed; see boost-build.log ($LASTEXITCODE)" }
} finally { Pop-Location }
if ($BoostOnly) { exit 0 }
$ssl = Join-Path $buildRoot 'cpython-bin-deps-1174fd43d858425c345b16d631716fb98ba492ea/amd64'
& $cmake -S (Join-Path $buildRoot 'libtorrent-2.0.15') -B (Join-Path $buildRoot 'build') -G 'Visual Studio 17 2022' -A x64 '-Dpython-bindings=ON' '-DBUILD_SHARED_LIBS=OFF' '-Dbuild_tests=OFF' '-Dbuild_examples=OFF' '-Dbuild_tools=OFF' '-Ddeprecated-functions=ON' '-Dpython-egg-info=OFF' "-DBOOST_ROOT=$boost" '-DBoost_USE_STATIC_LIBS=ON' '-DBoost_NO_SYSTEM_PATHS=ON' "-DPython3_ROOT_DIR=$python" "-DPython3_EXECUTABLE=$python/python.exe" "-DOPENSSL_ROOT_DIR=$ssl" "-DOPENSSL_INCLUDE_DIR=$ssl/include" "-DOPENSSL_SSL_LIBRARY=$ssl/libssl.lib" "-DOPENSSL_CRYPTO_LIBRARY=$ssl/libcrypto.lib" *> (Join-Path $buildRoot 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Configure failed; see configure.log ($LASTEXITCODE)" }
& $cmake --build (Join-Path $buildRoot 'build') --config Release --target python-libtorrent --parallel 4 *> (Join-Path $buildRoot 'native-build.log')
if ($LASTEXITCODE -ne 0) { throw "Native build failed; see native-build.log ($LASTEXITCODE)" }
