#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMasterData
{
    public interface ILocalMasterDataTable : IDisposable
    {
        string GetSheetName();

        byte[] CreateBinary(List<Dictionary<string, string>> records);

        void ReadBinary(byte[] bytes);
    }
}