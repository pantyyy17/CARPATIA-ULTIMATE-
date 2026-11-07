# Third-party notices

The root MIT license applies to original CARPATIA source code and documentation.
It does not relicense third-party software, fonts or imagery.

## Bundled mountain photograph

`src/Carpatia.Ultimate.App/Assets/alpine.jpg` is an Unsplash photograph, used under
the [Unsplash License](https://unsplash.com/license). It is not covered by this
repository's MIT grant. The original source URL and visual-reference notes are in
[asset credits](docs/asset-credits.md). Retain the asset-specific notice when
redistributing the image. No image is fetched by the running application.

## Microsoft components

.NET, System.Management, Windows App SDK components and their transitive
dependencies retain their upstream licenses and redistribution requirements.
See the license and notice files included in the restored NuGet packages and
any distributed self-contained runtime. Windows system icon fonts remain
subject to their Microsoft terms; this repository does not redistribute them.

## Development and test tools

xUnit, Microsoft.NET.Test.Sdk, the Visual Studio test adapter and GitHub Actions
retain their own upstream licenses. Test tooling is not an application runtime
dependency. The temporary Linux validation tools and SDK downloads are not
included in this repository.

Before distributing installers or self-contained binaries, retain all required
upstream notices. This source milestone does not provide a signed installer.
