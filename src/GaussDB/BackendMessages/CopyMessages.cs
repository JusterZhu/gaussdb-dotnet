using System;
using System.Collections.Generic;
using HuaweiCloud.GaussDB.Internal;

namespace HuaweiCloud.GaussDB.BackendMessages;

abstract class CopyResponseMessageBase : IBackendMessage
{
    public abstract BackendMessageCode Code { get; }

    internal bool IsBinary { get; private set; }
    internal short NumColumns { get; private set; }
    internal List<DataFormat> ColumnFormatCodes { get; }

    internal CopyResponseMessageBase()
        => ColumnFormatCodes = [];

    internal void Load(GaussDBReadBuffer buf)
    {
        ColumnFormatCodes.Clear();

        var binaryIndicator = buf.ReadByte();
        IsBinary = binaryIndicator switch
        {
            0 => false,
            1 => true,
            _ => throw new Exception("Invalid binary indicator in CopyInResponse message: " + binaryIndicator)
        };

        NumColumns = buf.ReadInt16();
        for (var i = 0; i < NumColumns; i++)
            ColumnFormatCodes.Add(DataFormatUtils.Create(buf.ReadInt16()));
    }
}

sealed class CopyInResponseMessage : CopyResponseMessageBase
{
    public override BackendMessageCode Code => BackendMessageCode.CopyInResponse;

    internal new CopyInResponseMessage Load(GaussDBReadBuffer buf)
    {
        base.Load(buf);
        return this;
    }
}

sealed class CopyOutResponseMessage : CopyResponseMessageBase
{
    public override BackendMessageCode Code => BackendMessageCode.CopyOutResponse;

    internal new CopyOutResponseMessage Load(GaussDBReadBuffer buf)
    {
        base.Load(buf);
        return this;
    }
}

sealed class CopyBothResponseMessage : CopyResponseMessageBase
{
    public override BackendMessageCode Code => BackendMessageCode.CopyBothResponse;

    internal new CopyBothResponseMessage Load(GaussDBReadBuffer buf)
    {
        base.Load(buf);
        return this;
    }
}

/// <summary>
/// Note that this message doesn't actually contain the data, but only the length. Data is processed
/// directly from the connector's buffer.
/// </summary>
sealed class CopyDataMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.CopyData;

    public int Length { get; private set; }

    internal CopyDataMessage Load(int len)
    {
        Length = len;
        return this;
    }
}

sealed class CopyDoneMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.CopyDone;
    internal static readonly CopyDoneMessage Instance = new();
    CopyDoneMessage() { }
}
