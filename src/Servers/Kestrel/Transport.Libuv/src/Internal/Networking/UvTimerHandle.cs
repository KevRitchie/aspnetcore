// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking
{
    internal class UvTimerHandle : UvHandle
    {
        private static readonly LibuvFunctions.uv_timer_cb _uv_timer_cb = UvTimerCb;

        private Action<UvTimerHandle> _callback;

        public UvTimerHandle(ILogger logger) : base(logger)
        {
        }

        public void Init(UvLoopHandle loop, Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            CreateHandle(
                loop.Libuv,
                loop.ThreadId,
                loop.Libuv.handle_size(LibuvFunctions.HandleType.TIMER),
                queueCloseHandle);

            _uv.timer_init(loop, this);
        }

        public void Start(Action<UvTimerHandle> callback, long timeout, long repeat)
        {
            _callback = callback;
            _uv.timer_start(this, _uv_timer_cb, timeout, repeat);
        }

        public void Stop()
        {
            _uv.timer_stop(this);
        }

        private static void UvTimerCb(IntPtr handle)
        {
            var timer = FromIntPtr<UvTimerHandle>(handle);

            try
            {
                timer._callback(timer);
            }
            catch (Exception ex)
            {
                timer._log.LogError(0, ex, nameof(UvTimerCb));
                throw;
            }
        }
    }
}
