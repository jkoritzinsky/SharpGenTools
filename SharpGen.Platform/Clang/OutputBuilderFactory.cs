// Copyright (c) Microsoft and Contributors. All rights reserved. Licensed under the University of Illinois/NCSA Open Source License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using SharpGen.Platform.Clang.Abstractions;
using SharpGen.Platform.Clang.CSharp;

namespace SharpGen.Platform.Clang
{
    internal sealed class OutputBuilderFactory
    {
        private readonly PInvokeGeneratorOutputMode _mode;
        private readonly Dictionary<string, IOutputBuilder> _outputBuilders;

        public OutputBuilderFactory(PInvokeGeneratorOutputMode mode)
        {
            _mode = mode;
            _outputBuilders = new Dictionary<string, IOutputBuilder>();
        }

        public IEnumerable<IOutputBuilder> OutputBuilders => _outputBuilders.Values;

        public void Clear()
        {
            _outputBuilders.Clear();
        }

        public IOutputBuilder Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var outputBuilder = new SharpGenOutputBuilder(new CSharpOutputBuilder(name));

            _outputBuilders.Add(name, outputBuilder);
            return outputBuilder;
        }

        public CSharpOutputBuilder CreateTests(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var outputBuilder = new CSharpOutputBuilder(name, isTestOutput: true);

            _outputBuilders.Add(name, outputBuilder);
            return outputBuilder;
        }

        public IOutputBuilder GetOutputBuilder(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            return _outputBuilders[name];
        }

        public CSharpOutputBuilder GetTestOutputBuilder(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (_outputBuilders[name] is CSharpOutputBuilder csharpOutputBuilder && csharpOutputBuilder.IsTestOutput)
            {
                return csharpOutputBuilder;
            }

            throw new ArgumentException("A test output builder was not found with the given name", nameof(name));
        }

        public bool TryGetOutputBuilder(string name, out IOutputBuilder outputBuilder)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            return _outputBuilders.TryGetValue(name, out outputBuilder);
        }
    }
}
