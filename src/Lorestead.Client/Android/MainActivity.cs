using Android.App;
using Android.Content.PM;
using Android.Views;
using Galdr.Native;

namespace Lorestead.Client;

// The one platform-specific source file: Android instantiates a manifest-declared
// activity instead of calling Main, so this stub routes the OS entry back into
// Program.Main.
[Activity(Label = "@string/app_name", MainLauncher = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout |
        ConfigChanges.KeyboardHidden | ConfigChanges.UiMode | ConfigChanges.Density)]
public class MainActivity : GaldrActivity
{
    protected override void RunMain() => Program.Main([]);
}
