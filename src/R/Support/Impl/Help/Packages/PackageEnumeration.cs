﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.R.Support.Help.Definitions;
using Microsoft.R.Support.Help.Functions;

namespace Microsoft.R.Support.Help.Packages {
    /// <summary>
    /// Implements enumerator of packages that is based
    /// on the particular collection install path.
    /// Package names normally match names of folders
    /// the packages are installed in.
    /// </summary>
    internal class PackageEnumeration : IEnumerable<IPackageInfo> {
        private readonly IFunctionIndex _functionIndex;
        private readonly string _libraryPath;

        public PackageEnumeration(IFunctionIndex functionIndex, string libraryPath) {
            _libraryPath = libraryPath;
            _functionIndex = functionIndex;
        }

        public IEnumerator<IPackageInfo> GetEnumerator() {
            return new PackageEnumerator(_functionIndex, _libraryPath);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }
    }

    class PackageEnumerator : IEnumerator<IPackageInfo> {
        private readonly IFunctionIndex _functionIndex;
        private IEnumerator<string> _directoriesEnumerator;

        public PackageEnumerator(IFunctionIndex functionIndex, string libraryPath) {
            _functionIndex = functionIndex;
            if (!string.IsNullOrEmpty(libraryPath) && Directory.Exists(libraryPath)) {
                _directoriesEnumerator = Directory.EnumerateDirectories(libraryPath).GetEnumerator();
            } else {
                _directoriesEnumerator = (new List<string>()).GetEnumerator();
            }
        }

        public IPackageInfo Current {
            get {
                string directoryPath = _directoriesEnumerator.Current;
                if (!string.IsNullOrEmpty(directoryPath)) {
                    string name = Path.GetFileName(directoryPath);
                    return new PackageInfo(_functionIndex, name, Path.GetDirectoryName(directoryPath));
                }

                return null;
            }
        }

        object IEnumerator.Current => this.Current;

        public bool MoveNext() {
            return _directoriesEnumerator.MoveNext();
        }

        public void Reset() {
            _directoriesEnumerator.Reset();
        }

        public void Dispose() {
            if (_directoriesEnumerator != null) {
                _directoriesEnumerator.Dispose();
                _directoriesEnumerator = null;
            }
        }
    }
}
