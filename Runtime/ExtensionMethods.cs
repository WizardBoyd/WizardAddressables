using System;
using System.Collections.Generic;
using UnityEngine;

namespace WizardAddressables.Runtime
{
    internal static class ExtensionMethods
    {
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if(source == null)
                throw new ArgumentNullException(nameof(source));
            if(action == null)
                throw new ArgumentNullException(nameof(action));
            
            foreach (T element in source)
                action(element);
        }
    }
}
