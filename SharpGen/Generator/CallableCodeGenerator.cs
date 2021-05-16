﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpGen.Logging;
using SharpGen.Model;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SharpGen.Generator
{
    internal sealed class CallableCodeGenerator : MemberCodeGeneratorBase<CsCallable>
    {
        public override IEnumerable<MemberDeclarationSyntax> GenerateCode(CsCallable csElement)
        {
            // method signature
            var parameters = csElement.PublicParameters.Select(
                param => GetMarshaller(param)
                        .GenerateManagedParameter(param)
                        .WithDefault(
                             param.DefaultValue == null
                                 ? default
                                 : EqualsValueClause(ParseExpression(param.DefaultValue))
                         )
            );

            var methodDeclaration = AddDocumentationTrivia(
                MethodDeclaration(
                        ParseTypeName(csElement.PublicReturnTypeQualifiedName),
                        csElement.Name
                    )
                   .WithModifiers(csElement.VisibilityTokenList.Add(Token(SyntaxKind.UnsafeKeyword)))
                   .WithParameterList(ParameterList(SeparatedList(parameters))),
                csElement
            );

            if (csElement.SignatureOnly)
            {
                yield return methodDeclaration
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                    .WithModifiers(TokenList());
                yield break;
            }

            StatementSyntaxList statements = new(Generators.Marshalling);

            foreach (var param in csElement.Parameters)
            {
                var relations = param.Relations;

                if (relations.Count != 0 || param.UsedAsReturn)
                    statements.Add(GenerateManagedHiddenMarshallableProlog(param));

                foreach (var relation in relations)
                {
                    if (!ValidRelationInScenario(relation))
                    {
                        Logger.Error(
                            LoggingCodes.InvalidRelationInScenario,
                            $"The relation \"{relation}\" is invalid in a method/function."
                        );
                        continue;
                    }

                    CsParameter relatedParameter = null;

                    if (relation is LengthRelation {Identifier: {Length: >0} relatedMarshallableName})
                    {
                        relatedParameter = csElement.Parameters
                                                    .SingleOrDefault(
                                                         p => p.CppElementName == relatedMarshallableName
                                                     );

                        if (relatedParameter is null)
                        {
                            Logger.Error(
                                LoggingCodes.InvalidRelationInScenario,
                                $"The relation with \"{relatedMarshallableName}\" parameter is invalid in a method/function \"{csElement.Name}\"."
                            );
                            continue;
                        }
                    }

                    statements.Add(
                        relation,
                        item => GetRelationMarshaller(item).GenerateManagedToNative(relatedParameter, param)
                    );
                }

                statements.AddRange(
                    param,
                    static(marshaller, item) => marshaller.GenerateManagedToNativeProlog(item)
                );
            }

            if (csElement.HasReturnType)
            {
                statements.Add(GenerateManagedHiddenMarshallableProlog(csElement.ReturnValue));
                statements.AddRange(
                    csElement.ReturnValue,
                    static(marshaller, item) => marshaller.GenerateManagedToNativeProlog(item)
                );
            }

            statements.AddRange(
                csElement.InRefInRefParameters,
                static (marshaller, item) => marshaller.GenerateManagedToNative(item, true)
            );

            var fixedStatements = csElement.PublicParameters
                .Select(param => GetMarshaller(param).GeneratePin(param))
                .Where(stmt => stmt != null).ToList();

            var callStmt = GeneratorHelpers.GetPlatformSpecificStatements(
                GlobalNamespace, Generators.Config, csElement.InteropSignatures.Keys,
                platform => ExpressionStatement(
                    Generators.NativeInvocation.GenerateCall(
                        csElement, platform, csElement.InteropSignatures[platform]
                    )
                )
            );

            var fixedStatement = fixedStatements.FirstOrDefault()?.WithStatement(callStmt);
            foreach (var statement in fixedStatements.Skip(1))
            {
                fixedStatement = statement.WithStatement(fixedStatement);
            }

            statements.Add(fixedStatement ?? callStmt);

            statements.AddRange(
                csElement.LocalManagedReferenceParameters,
                static (marshaller, item) => marshaller.GenerateNativeToManaged(item, true)
            );

            if (csElement.HasReturnType)
                statements.Add(
                    csElement.ReturnValue,
                    static (marshaller, item) => marshaller.GenerateNativeToManaged(item, true)
                );

            statements.AddRange(
                csElement.InRefInRefParameters,
                static (marshaller, item) => marshaller.GenerateNativeCleanup(item, true)
            );

            if (csElement.IsReturnTypeResult && csElement.CheckReturnType)
            {
                statements.Add(ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(csElement.ReturnValue.Name),
                        IdentifierName("CheckError")))));
            }

            // Return
            if (csElement.HasReturnStatement)
            {
                statements.Add(ReturnStatement(IdentifierName(csElement.ReturnName)));
            }

            yield return methodDeclaration.WithBody(statements.ToBlock());
        }

        private static StatementSyntax GenerateManagedHiddenMarshallableProlog(CsMarshalCallableBase csElement)
        {
            var elementType = ParseTypeName(csElement.PublicType.QualifiedName);

            return LocalDeclarationStatement(
                VariableDeclaration(
                    csElement.IsArray
                        ? ArrayType(elementType, SingletonList(ArrayRankSpecifier()))
                        : elementType,
                    SingletonSeparatedList(VariableDeclarator(csElement.Name))
                )
            );
        }

        private static bool ValidRelationInScenario(MarshallableRelation relation) =>
            relation is ConstantValueRelation or LengthRelation;

        public CallableCodeGenerator(Ioc ioc) : base(ioc)
        {
        }
    }
}
