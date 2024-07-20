using System;

namespace WizardAddressables.Runtime.Exceptions
{
    public class ConversionException : Exception
    {
        public ConversionException(string message) : base(message)
        {
        }
    }
}