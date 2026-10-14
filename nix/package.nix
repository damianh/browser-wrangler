{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  expat,
  fontconfig,
  freetype,
  libice,
  libsm,
  libx11,
  libxcursor,
  libxext,
  libxi,
  libxkbcommon,
  libxrandr,
  libxrender,
  openssl,
  zlib,
}:

buildDotnetModule {
  pname = "browser-wrangler";
  version = "1.0.0";

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../src/BrowserWrangler.Core
      ../src/BrowserWrangler.Linux
      ../installer/linux/browser-wrangler.desktop
    ];
  };

  projectFile = "src/BrowserWrangler.Linux/BrowserWrangler.Linux.csproj";
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  # Nix builds a regular framework-dependent app; Native AOT is for the tarball and AppImage.
  dotnetFlags = [ "-p:PublishAot=false" ];

  executables = [ "browser-wrangler" ];

  # Native libraries loaded at runtime by Avalonia, SkiaSharp and System.Net.Security.
  runtimeDeps = [
    expat
    fontconfig
    freetype
    libice
    libsm
    libx11
    libxcursor
    libxext
    libxi
    libxkbcommon
    libxrandr
    libxrender
    openssl
    zlib
  ];

  # The packaged .desktop file launches "browser-wrangler" from PATH; keep registration in step
  # with it rather than pinning a store path that changes on every update.
  makeWrapperArgs = [
    "--set"
    "BROWSER_WRANGLER_EXEC"
    "browser-wrangler"
  ];

  postInstall = ''
    install -Dm644 installer/linux/browser-wrangler.desktop $out/share/applications/browser-wrangler.desktop
    install -Dm644 src/BrowserWrangler.Linux/Assets/logo-256.png $out/share/icons/hicolor/256x256/apps/browser-wrangler.png
  '';

  meta = {
    description = "Routes the links you click to the right browser and profile, based on rules you define";
    homepage = "https://github.com/damianh/browser-wrangler";
    license = lib.licenses.asl20;
    mainProgram = "browser-wrangler";
    platforms = lib.platforms.linux;
  };
}
