{
  description = "Browser Wrangler: routes the links you click to the right browser and profile";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      packages = forAllSystems (pkgs: rec {
        browser-wrangler = pkgs.callPackage ./nix/package.nix { };
        default = browser-wrangler;
      });

      apps = forAllSystems (pkgs: rec {
        browser-wrangler = {
          type = "app";
          program = "${self.packages.${pkgs.stdenv.hostPlatform.system}.browser-wrangler}/bin/browser-wrangler";
        };
        default = browser-wrangler;
      });

      overlays.default = final: _prev: {
        browser-wrangler = final.callPackage ./nix/package.nix { };
      };

      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = [
            pkgs.dotnetCorePackages.sdk_10_0
            pkgs.clang
            pkgs.lld
            pkgs.zlib
          ];
        };
      });
    };
}
