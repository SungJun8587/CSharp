using Microsoft.AspNetCore.Mvc.Formatters;
using Google.Protobuf;
using Microsoft.Net.Http.Headers;

/// <summary>
/// HTTP 요청 본문을 Protobuf 메시지 형식으로 역직렬화하는 Input Formatter입니다.
/// Content-Type: application/x-protobuf를 처리합니다.
/// </summary>
public class ProtobufInputFormatter : InputFormatter
{
    public ProtobufInputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/x-protobuf"));
    }

    /// <summary>
    /// 요청된 타입이 Protobuf IMessage 인터페이스를 상속받는지 확인합니다.
    /// </summary>
    protected override bool CanReadType(Type type)
    {
        return typeof(IMessage).IsAssignableFrom(type);
    }

    /// <summary>
    /// 요청 본문을 읽어 Protobuf 메시지로 변환합니다.
    /// </summary>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        var type = context.ModelType;

        // 1. IMessage.Parser 정적 속성을 사용하여 MessageParser를 가져옵니다.
        var parser = type.GetProperty("Parser")?.GetValue(null) as MessageParser;
        if (parser == null)
        {
            return await InputFormatterResult.FailureAsync();
        }

        // 2. [수정된 부분]: 요청 본문 전체를 비동기적으로 바이트 배열로 읽습니다.
        byte[] buffer;
        using (var memoryStream = new MemoryStream())
        {
            // request.Body를 비동기적으로 메모리 스트림에 복사
            await request.Body.CopyToAsync(memoryStream);
            buffer = memoryStream.ToArray();
        }

        // 3. 바이트 배열을 사용하여 Protobuf 메시지를 파싱합니다.
        try
        {
            // ParseFrom(byte[]) 오버로드는 비동기 I/O 문제에서 벗어날 수 있습니다.
            var message = parser.ParseFrom(buffer);
            return await InputFormatterResult.SuccessAsync(message);
        }
        catch (Exception ex)
        {
            // 파싱 오류 발생 시
            context.ModelState.AddModelError(string.Empty, $"Protobuf deserialization failed: {ex.Message}");
            return await InputFormatterResult.FailureAsync();
        }
    }
}