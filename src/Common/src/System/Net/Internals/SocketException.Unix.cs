// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Net.Sockets;

namespace System.Net.Internals
{
    // TODO #2891: Add a public ctor to SocketException instead.
    internal class InternalSocketException : SocketException
    {
        public InternalSocketException(SocketError errorCode, int platformError)
            : base((int)errorCode)
        {
            HResult = platformError;
        }
    }
}
