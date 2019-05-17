﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SharpGen.Model
{
    [Flags]
    public enum PlatformDetectionType
    {
        IsWindows = 0b000001,
        IsSystemV = 0b000010,
        Any = IsWindows | IsSystemV
    }
}
