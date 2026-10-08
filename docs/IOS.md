# Running on the iOS simulator

Needs Xcode (run it once to accept the licence and install an iOS simulator runtime) and the iOS workload
(`sudo dotnet workload restore src/PasswordKeeper.App/PasswordKeeper.App/PasswordKeeper.App.csproj`).

```
xcrun simctl list devices available        # pick an iPhone and note its UDID
xcrun simctl boot <UDID>; open -a Simulator
cd src/PasswordKeeper.App/PasswordKeeper.App
dotnet build -f net10.0-ios -t:Run -p:RuntimeIdentifier=iossimulator-arm64 -p:_DeviceName=:v2:udid=<UDID>
```
Use `iossimulator-x64` on an Intel Mac. The vault is stored inside the app's own sandbox on the device.

## Layout
Below 700 px wide the app shows one pane at a time (entry list, then the editor with an "All entries" back button).

## Not done yet
Face ID / Touch ID, signing for a real device, and moving the vault file in and out of the phone
(Files app / iCloud / Backblaze) still need work.
