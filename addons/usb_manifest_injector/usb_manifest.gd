@tool
extends EditorPlugin

var export_plugin : AndroidUSBExportPlugin

func _enter_tree():
    export_plugin = AndroidUSBExportPlugin.new()
    add_export_plugin(export_plugin)

func _exit_tree():
    remove_export_plugin(export_plugin)
    export_plugin = null

class AndroidUSBExportPlugin extends EditorExportPlugin:
    func _get_name():
        return "AndroidUSBManifestInjector"

    func _supports_platform(platform):
        return platform is EditorExportPlatformAndroid

    # 这个内置函数会在导出时自动把返回值写入 AndroidManifest.xml 的 <manifest> 标签下
    func _get_android_manifest_element_contents(platform, debug) -> String:
        return """
        <!-- 允许设备作为 USB 主机 -->
        <uses-feature android:name="android.hardware.usb.host" android:required="true" />
        <!-- 声明 USB 权限 -->
        <uses-permission android:name="android.permission.USB_PERMISSION" />
        """