using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Toolkit.Mvvm;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Mvvm.Messaging;

using elaphureLink.Wpf.Core.Services;
using elaphureLink.Wpf.Messenger;

namespace elaphureLink.Wpf.Core
{
    class elaphureLinkCore
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static SettingsService _SettingService = new SettingsService();

        ////
        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_init",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern System.Int32 el_proxy_init();

        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_start_with_address",
            CallingConvention = CallingConvention.Cdecl,
            CharSet = CharSet.Ansi
        )]
        private static extern System.Int32 el_proxy_start_with_address(string address);

        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_stop",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern void el_proxy_stop();

        // on disconnect callback type
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OnProxyDisconnectCallbackType(
            [MarshalAs(UnmanagedType.LPStr)] string messageFromProxy
        );

        // set on disconnect callback function
        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_set_on_disconnect_callback",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern void el_proxy_set_on_disconnect_callback(
            [MarshalAs(UnmanagedType.FunctionPtr)] OnProxyDisconnectCallbackType callbackPointer
        );

        // Auto-reconnect functions
        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_set_auto_reconnect",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern void el_proxy_set_auto_reconnect(bool enabled);

        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_is_connected",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern bool el_proxy_is_connected();

        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_get_device_address",
            CallingConvention = CallingConvention.Cdecl,
            CharSet = CharSet.Ansi
        )]
        private static extern int el_proxy_get_device_address(
            [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder buffer,
            int bufferSize
        );

        private static OnProxyDisconnectCallbackType OnProxyDisconnectCallback = (msg) =>
        {
            Logger.Info(msg);

            WeakReferenceMessenger.Default.Send(new ProxyStatusChangedMessage(false));
        };

        [StructLayout(LayoutKind.Explicit)]
        public struct proxyConfig
        {
            [FieldOffset(0)]
            public byte enable_vendor_command;
        }

        [DllImport(
            "elaphureLinkProxy.dll",
            EntryPoint = "el_proxy_change_config",
            CallingConvention = CallingConvention.Cdecl
        )]
        private static extern void el_proxy_change_config(ref proxyConfig config);

        //////

        public static async Task ChangeProxyConfigAsync(bool enable_vendor_command)
        {
            await Task.Factory.StartNew(() =>
            {
                proxyConfig config = new proxyConfig
                {
                    enable_vendor_command = Convert.ToByte(enable_vendor_command)
                };

                el_proxy_change_config(ref config);
            });
        }

        public static async Task StartProxyAsync(string deviceAddress)
        {
            Logger.Info("Launch proxy in progress");

            await Task.Factory.StartNew(() =>
            {
                int ret = el_proxy_init();
                if (ret != 0)
                {
                    Logger.Error("Can not init proxy");

                    WeakReferenceMessenger.Default.Send(new ProxyStatusChangedMessage(false));
                    return;
                }
                else
                {
                    Logger.Debug("Proxy init successed");
                }

                proxyConfig config = new proxyConfig
                {
                    enable_vendor_command = Convert.ToByte(
                        _SettingService.GetValue<bool>("EnableVendorCommand"))
                };
                el_proxy_change_config(ref config);

                ret = el_proxy_start_with_address(deviceAddress);
                if (ret != 0)
                {
                    Logger.Error("Failed to start proxy, perhaps an invalid address?");

                    WeakReferenceMessenger.Default.Send(new ProxyStatusChangedMessage(false));
                    return;
                }

                el_proxy_set_on_disconnect_callback(OnProxyDisconnectCallback);

                Logger.Info("Proxy has started successfully");

                WeakReferenceMessenger.Default.Send(new ProxyStatusChangedMessage(true));
            });
        }

        public static async Task StopProxyAsync()
        {
            WeakReferenceMessenger.Default.Send(new ProxyStatusChangedMessage(false));
            await Task.Factory.StartNew(() => el_proxy_stop());
        }

        /// <summary>
        /// Enable or disable auto-reconnect functionality
        /// </summary>
        /// <param name="enabled">True to enable auto-reconnect, false to disable</param>
        public static void SetAutoReconnect(bool enabled)
        {
            Logger.Info($"Auto-reconnect {(enabled ? "enabled" : "disabled")}");
            el_proxy_set_auto_reconnect(enabled);
        }

        /// <summary>
        /// Check if proxy is currently connected
        /// </summary>
        /// <returns>True if connected, false otherwise</returns>
        public static bool IsProxyConnected()
        {
            return el_proxy_is_connected();
        }

        /// <summary>
        /// Get the current device address being used by the proxy
        /// </summary>
        /// <returns>Device address string, or empty string if not set</returns>
        public static string GetCurrentDeviceAddress()
        {
            var buffer = new System.Text.StringBuilder(256);
            int result = el_proxy_get_device_address(buffer, buffer.Capacity);

            if (result == 0)
            {
                return buffer.ToString();
            }

            Logger.Warning($"Failed to get device address, error code: {result}");
            return string.Empty;
        }
    }
}
