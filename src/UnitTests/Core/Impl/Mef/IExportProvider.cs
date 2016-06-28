﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.UnitTests.Core.Mef {
    public interface IExportProvider : IDisposable {
        T GetExportedValue<T>();
        IEnumerable<Lazy<T>> GetExports<T>();
        IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>();
    }
}