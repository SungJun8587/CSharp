using Google.Protobuf;
using System.Net.Http.Headers;

namespace JwtAuthService.Protobuf.Helper
{
    public static class ProtobufHelper
    {
        // Protobuf 메시지를 직렬화하여 HTTP 요청을 생성하는 헬퍼 함수
        public static ByteArrayContent CreateProtobufContent<T>(T message) where T : IMessage<T>
        {
            var bytes = message.ToByteArray();
            var content = new ByteArrayContent(bytes);
            // 요청 본문의 Content-Type을 Protobuf로 지정
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
            return content;
        }

        // HTTP 응답 본문(바이트 배열)을 Protobuf 메시지로 역직렬화하는 헬퍼 함수
        public static async Task<T?> ReadProtobufAsync<T>(HttpResponseMessage response) where T : IMessage<T>, new()
        {
            if (response.Content.Headers.ContentType?.MediaType != "application/x-protobuf")
            {
                return default;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return (T)new T().Descriptor.Parser.ParseFrom(bytes);
        }
    }
}
