using Microsoft.AspNetCore.Mvc.Formatters;
using Google.Protobuf;
using Microsoft.Net.Http.Headers;

/// <summary>
/// 응답 객체를 Protobuf 메시지 형식으로 직렬화하는 Output Formatter입니다.
/// Content-Type: application/x-protobuf로 응답합니다.
/// </summary>
public class ProtobufOutputFormatter : OutputFormatter
{
    public ProtobufOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/x-protobuf"));
    }

    /// <summary>
    /// 응답 타입이 Protobuf IMessage 인터페이스를 상속받는지 확인합니다.
    /// </summary>
    protected override bool CanWriteType(Type? type)
    {
        return type != null && typeof(IMessage).IsAssignableFrom(type);
    }

    /// <summary>
    /// Protobuf 메시지를 HTTP 응답 본문에 씁니다.
    /// </summary>
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
        var response = context.HttpContext.Response;

        if (context.Object is IMessage message)
        {
            // Protobuf 메시지를 바이트로 직렬화합니다.
            using var memoryStream = new MemoryStream();
            message.WriteTo(memoryStream);
            memoryStream.Position = 0;

            // 직렬화된 바이트를 응답 스트림에 씁니다.
            await response.Body.WriteAsync(memoryStream.ToArray());
        }
    }
}