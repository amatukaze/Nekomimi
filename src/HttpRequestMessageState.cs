namespace Sakuno.Nekomimi
{
    internal enum HttpRequestMessageState
    {
        ParsingMethod,
        ParsingRequestUri,
        ParsingVersion,
        ParsingHeader,
        HeadersParsed,

        ReadingBody,

        MessageParsed,
    }
}
