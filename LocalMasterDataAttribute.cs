#nullable enable
using System;

namespace LocalMasterData
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class LocalMasterDataAttribute : Attribute
    {
        public string SheetName { get; }

        public LocalMasterDataAttribute(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                throw new ArgumentException("Sheet name must not be empty.", nameof(sheetName));
            }

            SheetName = sheetName;
        }
    }
}
