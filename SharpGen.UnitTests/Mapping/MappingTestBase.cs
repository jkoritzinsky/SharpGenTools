﻿using System.Collections.Generic;
using SharpGen.Config;
using SharpGen.CppModel;
using SharpGen.Model;
using SharpGen.Transform;
using Xunit.Abstractions;

namespace SharpGen.UnitTests.Mapping
{
    public abstract class MappingTestBase : TestBase
    {
        protected MappingTestBase(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
        }

        protected (CsAssembly Assembly, IEnumerable<DefineExtensionRule> Defines) MapModel(CppModule module, ConfigFile config)
        {
            var transformer = CreateTransformer();

            return transformer.Transform(module, ConfigFile.Load(config, new string[0], Logger));
        }

        protected (IEnumerable<BindRule> bindings, IEnumerable<DefineExtensionRule> defines) GetConsumerBindings(CppModule module, ConfigFile config)
        {
            var transformer = CreateTransformer();

            transformer.Transform(module, ConfigFile.Load(config, new string[0], Logger));

            return transformer.GenerateTypeBindingsForConsumers();
        }

        private TransformManager CreateTransformer()
        {
            NamingRulesManager namingRules = new();

            // Run the main mapping process
            return new TransformManager(
                namingRules,
                new ConstantManager(namingRules, Ioc),
                Ioc
            );
        }
    }
}
