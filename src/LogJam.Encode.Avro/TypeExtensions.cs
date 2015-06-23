﻿// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License.  You may obtain a copy
// of the License at http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED
// WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
// 
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

// This file is based on TypeExtensions.cs from Microsoft.Hadoop.Avro in https://hadoopsdk.codeplex.com/
namespace LogJam.Encode.Avro
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.RegularExpressions;

    using LogJam.Schema;


    internal static class TypeExtensions
    {
        /// <summary>
        /// Checks if <paramref name="type"/> has a public parameter-less constructor.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>True if type t has a public parameter-less constructor, false otherwise.</returns>
        public static bool HasParameterlessConstructor(this Type type)
        {
            return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
        }

        /// <summary>
        /// The natively supported types for Avro serialization.
        /// </summary>
        private static readonly HashSet<Type> NativelySupported = new HashSet<Type>
        {
            typeof(char),
            typeof(byte),
            typeof(sbyte),
            typeof(short),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(bool),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal),
            typeof(string),
            typeof(Uri),
            typeof(byte[]),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(Guid)
        };

        public static bool IsNativelySupported(this Type type)
        {
            var notNullable = Nullable.GetUnderlyingType(type) ?? type;
            return NativelySupported.Contains(notNullable)
                || type.IsArray
                || type.IsKeyValuePair()
                || type.GetAllInterfaces()
                       .FirstOrDefault(t => t.IsGenericType && 
                                            t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) != null;
        }

        private static readonly HashSet<Type> SupportedInterfaces = new HashSet<Type>
        {
            typeof(IList<>),
            typeof(IDictionary<,>)
        };

        public static bool IsAnonymous(this Type type)
        {
            return type.IsClass
                && type.GetCustomAttributes(false).Any(a => a is CompilerGeneratedAttribute)
                && !type.IsNested
                && type.Name.StartsWith("<>", StringComparison.Ordinal)
                && type.Name.Contains("__Anonymous");
        }

        public static PropertyInfo GetPropertyByName(
            this Type type, string name, BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance)
        {
            return type.GetProperty(name, flags);
        }

        public static MethodInfo GetMethodByName(this Type type, string shortName, params Type[] arguments)
        {
            var result = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SingleOrDefault(m => m.Name == shortName && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments));

            if (result != null)
            {
                return result;
            }

            return
                type
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => (m.Name.EndsWith(shortName, StringComparison.Ordinal) ||
                                       m.Name.EndsWith("." + shortName, StringComparison.Ordinal))
                                 && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments));
        }

        /// <summary>
        /// Gets all fields of the type.
        /// </summary>
        /// <param name="t">The type.</param>
        /// <returns>Collection of fields.</returns>
        public static IEnumerable<FieldInfo> GetAllFields(this Type t)
        {
            if (t == null)
            {
                return Enumerable.Empty<FieldInfo>();
            }

            const BindingFlags Flags = 
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly;
            return t
                .GetFields(Flags)
                .Where(f => !f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .Concat(GetAllFields(t.BaseType));
        }

        /// <summary>
        /// Gets all properties of the type.
        /// </summary>
        /// <param name="t">The type.</param>
        /// <returns>Collection of properties.</returns>
        public static IEnumerable<PropertyInfo> GetAllProperties(this Type t)
        {
            if (t == null)
            {
                return Enumerable.Empty<PropertyInfo>();
            }

            const BindingFlags Flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly;

            return t
                .GetProperties(Flags)
                .Where(p => !p.IsDefined(typeof(CompilerGeneratedAttribute), false)
                            && p.GetIndexParameters().Length == 0)
                .Concat(GetAllProperties(t.BaseType));
        }

        public static IEnumerable<Type> GetAllInterfaces(this Type t)
        {
            foreach (var i in t.GetInterfaces())
            {
                yield return i;
            }
        }

        public static IEnumerable<TAttribute> GetCustomAttributes<TAttribute>(this MemberInfo member, bool inherit)
            where TAttribute : Attribute
        {
            return member.GetCustomAttributes(typeof(TAttribute), inherit).Cast<TAttribute>();
        }

        public static TAttribute GetCustomAttribute<TAttribute>(this MemberInfo member, bool inherit)
            where TAttribute : Attribute
        {
            return member.GetCustomAttributes<TAttribute>(inherit).FirstOrDefault();
        }

        public static void GetAvroTypeName(this Type type, out string name, out string @namespace)
        {
            Contract.Requires<ArgumentNullException>(type != null);

            var typeNameAttribute = type.GetCustomAttribute<SerializedTypeNameAttribute>(false);
            if (typeNameAttribute != null)
            {
                name = typeNameAttribute.Name;
                @namespace = typeNameAttribute.Namespace;
            }
            else
            {
                name = type.Name;
                @namespace = type.Namespace;
            }
        }

        public static string StripAvroNonCompatibleCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Regex.Replace(value, @"[^A-Za-z0-9_\.]", string.Empty, RegexOptions.None);
        }

        public static bool IsFlagEnum(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }
            return type.GetCustomAttributes(false).ToList().Find(a => a is FlagsAttribute) != null;
        }

        public static bool CanContainNull(this Type type)
        {
            return !type.IsValueType || (Nullable.GetUnderlyingType(type) != null);
        }

        public static bool IsKeyValuePair(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        }

        public static bool CanBeKnownTypeOf(this Type type, Type baseType)
        {
            return !type.IsAbstract
                   && (type.IsSubclassOf(baseType) 
                   || type == baseType 
                   || (baseType.IsInterface && baseType.IsAssignableFrom(type))
                   || (baseType.IsGenericType && baseType.IsInterface && baseType.GenericIsAssignable(baseType)
                           && type.GetGenericArguments()
                                  .Zip(baseType.GetGenericArguments(), (type1, type2) => new Tuple<Type, Type>(type1, type2))
                                  .ToList()
                                  .TrueForAll(tuple => CanBeKnownTypeOf(tuple.Item1, tuple.Item2))));
        }

        private static bool GenericIsAssignable(this Type type, Type instanceType)
        {
            if (!type.IsGenericType || !instanceType.IsGenericType)
            {
                return false;
            }

            var args = type.GetGenericArguments();
            return args.Any() && type.IsAssignableFrom(instanceType.GetGenericTypeDefinition().MakeGenericType(args));
        }

        public static bool IsNullable(this MemberInfo member)
        {
            Contract.Requires<ArgumentNullException>(member != null);

            var requiredAttribute = member.GetCustomAttribute<RequiredAttribute>(false);
            if (requiredAttribute != null)
            {
                // Presence of RequiredAttribute overrides whether the type is nullable or not
                return ! requiredAttribute.IsRequired;
            }
            else
            {
                switch (member.MemberType)
                {
                    case MemberTypes.Field:
                        return ((FieldInfo)member).FieldType.CanContainNull();
                    case MemberTypes.Property:
                        return ((PropertyInfo)member).PropertyType.CanContainNull();
                    default:
                        return true;
                }
            }
        }

        public static bool IgnoreDataMember(this MemberInfo member)
        {
            Contract.Requires<ArgumentNullException>(member != null);

            return member.GetCustomAttribute<IgnoreDataMemberAttribute>(false) != null;
        }

        public static void CheckPropertyGetters(IEnumerable<PropertyInfo> properties)
        {
            var missingGetter = properties.FirstOrDefault(p => p.GetGetMethod(true) == null);
            if (missingGetter != null)
            {
                throw new SerializationException(
                    string.Format(CultureInfo.InvariantCulture, "Property '{0}' of class '{1}' does not have a getter.", missingGetter.Name, missingGetter.DeclaringType.FullName));
            }
        }

        public static IList<PropertyInfo> RemoveDuplicates(IEnumerable<PropertyInfo> properties)
        {
            var result = new List<PropertyInfo>();
            foreach (var p in properties)
            {
                if (result.Find(s => s.Name == p.Name) == null)
                {
                    result.Add(p);
                }
            }

            return result;
        }
    }
}