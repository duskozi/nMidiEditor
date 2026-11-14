# Microsoft Store package

`nMIDIEditor.Store.sln` adds `Package/nMIDIEditor.Package.wapproj`, which packages the editor as MSIX for the
Microsoft Store. It needs Visual Studio on Windows with the **MSIX Packaging Tools** component of the .NET desktop
workload, so it is not part of `nMIDIEditor.sln` or the CI build.

1. Open `nMIDIEditor.Store.sln` and set `nMIDIEditor.Package` as the startup project (F5 installs and runs the
   package locally).
2. Right-click it, then **Publish > Associate App with the Store**, and pick the name reserved in Partner Center.
   This fills in the package identity in `Package.appxmanifest`.
3. **Publish > Create App Packages > Microsoft Store**, x64, Release. Upload the `.msixupload` file in Partner Center.

The package holds a self-contained build (`Properties/PublishProfiles/win-x64.pubxml`), so Store users don't need
.NET installed. The Store signs the package. Raise the version in `Package.appxmanifest` together with
`nMIDIEditor.csproj`; the Store wants the last number to be 0.
