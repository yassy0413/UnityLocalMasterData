#nullable enable
using System;

namespace LocalMasterData
{
    public readonly struct LocalMasterDataTableRegistration
    {
        public string SheetName { get; }
        public Type Type { get; }
        public ILocalMasterDataTable Instance { get; }

        public LocalMasterDataTableRegistration(string sheetName, Type type, ILocalMasterDataTable instance)
        {
            SheetName = sheetName;
            Type = type;
            Instance = instance;
        }
    }
}