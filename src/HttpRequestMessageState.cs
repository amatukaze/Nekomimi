namespace Sakuno.Nekomimi
{
    internal enum HttpRequestMessageState
    {
        ParsingMethod,
        ParsingRequestUri,
        ParsingVersion,
        ParsingHeader,
        HeadersParsed,

        HandlingExpect100Continue,
        ReadingBody,

        MessageParsed,
    }
}
