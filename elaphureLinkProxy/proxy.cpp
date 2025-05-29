#include "pch.h"

#include "SocketClient.hpp"
#include <thread>
#include <chrono>
#include <atomic>
#include <string>


class ProxyManager
{
    public:
    ProxyManager()
        : on_connect_callback_(nullptr),
          on_socket_disconnect_callback_(nullptr),
          auto_reconnect_enabled_(false),
          reconnect_attempts_(0),
          current_device_address_(""),
          is_reconnect_thread_running_(false)
    {
    }

    bool is_proxy_running()
    {
        if (client_.get()) {
            return client_.get()->is_socket_running();
        }

        return false;
    }

    void set_on_proxy_connect_callback(onSocketConnectCallbackType callback)
    {
        on_connect_callback_ = callback;

        if (client_.get()) {
            client_.get()->set_connect_callback(callback);
        }
    }

    void set_on_proxy_disconnect_callback(onSocketDisconnectCallbackType callback)
    {
        on_socket_disconnect_callback_ = callback;

        if (client_.get()) {
            client_.get()->set_disconnect_callback(callback);
        }
    }
    int start_with_address(std::string address)
    {
        // Store the address for auto-reconnect
        current_device_address_ = address;

        stop();
        client_ = std::make_unique<SocketClient>();

        if (on_connect_callback_) {
            client_.get()->set_connect_callback(on_connect_callback_);
        }
        if (on_socket_disconnect_callback_) {
            // Use wrapper to handle auto-reconnect
            client_.get()->set_disconnect_callback([this](const char *msg) {
                this->on_disconnect_wrapper(msg);
            });
        }

        int ret = client_.get()->init_socket(address, "3240");
        if (ret != 0) {
            return ret;
        }

        return client_.get()->start();
    }

    void stop()
    {
        if (client_.get()) {
            client_.get()->kill();
        }
        client_.reset(nullptr);
    }

    void set_auto_reconnect(bool enabled)
    {
        auto_reconnect_enabled_ = enabled;
    }

    bool is_connected()
    {
        return is_proxy_running();
    }

    int get_device_address(char *buffer, int bufferSize)
    {
        if (!buffer || bufferSize <= 0) {
            return -1;
        }

        if (current_device_address_.length() >= bufferSize) {
            return -2; // Buffer too small
        }

        strcpy_s(buffer, bufferSize, current_device_address_.c_str());
        return 0;
    }

    void on_disconnect_wrapper(const char *msg)
    {
        if (on_socket_disconnect_callback_) {
            on_socket_disconnect_callback_(msg);
        }

        // Start auto-reconnect if enabled
        if (auto_reconnect_enabled_ && !current_device_address_.empty() && !is_reconnect_thread_running_) {
            is_reconnect_thread_running_ = true;
            reconnect_attempts_          = 0;

            std::thread reconnect_thread([this]() {
                auto_reconnect_loop();
            });
            reconnect_thread.detach();
        }
    }

    void auto_reconnect_loop()
    {
        const int MAX_ATTEMPTS     = 10;
        const int INITIAL_DELAY_MS = 1000;

        while (auto_reconnect_enabled_ && reconnect_attempts_ < MAX_ATTEMPTS && !is_proxy_running()) {
            reconnect_attempts_++;

            // Calculate exponential backoff delay
            int delay_ms = INITIAL_DELAY_MS * (1 << (reconnect_attempts_ - 1));
            if (delay_ms > 30000)
                delay_ms = 30000; // Cap at 30 seconds

            std::this_thread::sleep_for(std::chrono::milliseconds(delay_ms));

            if (!auto_reconnect_enabled_) {
                break; // Auto-reconnect was disabled during wait
            }

            // Try to reconnect
            int result = start_with_address(current_device_address_);
            if (result == 0) {
                // Reconnection successful
                reconnect_attempts_          = 0;
                is_reconnect_thread_running_ = false;
                return;
            }
        }

        // If we reach here, either auto-reconnect was disabled or max attempts reached
        is_reconnect_thread_running_ = false;
        if (reconnect_attempts_ >= MAX_ATTEMPTS) {
            auto_reconnect_enabled_ = false; // Disable after max attempts
        }
    }
    void stop()
    {
        auto_reconnect_enabled_ = false; // Disable auto-reconnect when manually stopping
        current_device_address_.clear(); // Clear stored address

        if (client_.get()) {
            client_.get()->kill();
        }
        client_.reset(nullptr);
    }

    private:
    onSocketConnectCallbackType    on_connect_callback_;
    onSocketDisconnectCallbackType on_socket_disconnect_callback_;
    std::unique_ptr<SocketClient>  client_;

    // Auto-reconnect members
    std::atomic<bool> auto_reconnect_enabled_;
    std::atomic<int>  reconnect_attempts_;
    std::string       current_device_address_;
    std::atomic<bool> is_reconnect_thread_running_;
};

ProxyManager k_manager;


PROXY_DLL_FUNCTION int el_proxy_start_with_address(char *address)
{
    return k_manager.start_with_address(address);
}


PROXY_DLL_FUNCTION void el_proxy_stop()
{
    if (k_is_proxy_init) {
        return k_manager.stop();
    }
}


PROXY_DLL_FUNCTION void el_proxy_set_on_connect_callback(onSocketConnectCallbackType callback)
{
    return k_manager.set_on_proxy_connect_callback(callback);
}


PROXY_DLL_FUNCTION void el_proxy_set_on_disconnect_callback(onSocketDisconnectCallbackType callback)
{
    el_proxy_start_with_address(deviceAddress);
    return k_manager.set_on_proxy_disconnect_callback(callback);
}

PROXY_DLL_FUNCTION void el_proxy_set_auto_reconnect(bool enabled)
{
    return k_manager.set_auto_reconnect(enabled);
}

PROXY_DLL_FUNCTION bool el_proxy_is_connected()
{
    return k_manager.is_connected();
}

PROXY_DLL_FUNCTION int el_proxy_get_device_address(char *buffer, int bufferSize)
{
    return k_manager.get_device_address(buffer, bufferSize);
}
