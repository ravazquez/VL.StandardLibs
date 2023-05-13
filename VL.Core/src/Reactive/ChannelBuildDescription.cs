﻿using System;
using System.Linq;
using System.Reflection;

namespace VL.Core.Reactive
{
    record ChannelBuildDescription(string Name, string TypeName)
    {
        /// <summary>
        /// Returns object for patched types. Must bu used when building the pin description.
        /// </summary>
        public Type CompileTimeType
        { 
            get
            {
                var type = TypeRegistry.Default.GetTypeByName(TypeName) ?? typeof(object);
                // Is Patched?
                if (type.CustomAttributes.Any(c => c.AttributeType.Name == "ElementAttribute"))
                    return typeof(object);
                return type;
            }
        }

        /// <summary>
        /// Returns the type itself. Must not be called during compilation.
        /// </summary>
        public Type RuntimeType
        {
            get
            {
                return TypeRegistry.Default.GetTypeByName(TypeName) ?? typeof(object);
            }
        }
    }
}
