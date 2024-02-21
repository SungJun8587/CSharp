using Newtonsoft.Json;
using System.Text;

namespace Common.Lib
{
    public interface IHttpClientHelper<T>
    {
        Task<T> GetSingleItemRequest(string requestUri, Dictionary<string, string> fields, CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T[]> GetMultipleItemsRequest(string requestUri, Dictionary<string, string> fields,  CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T> PostRequest(string requestUri, T request, CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T> PostRequest(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T> PostRequest(string requestUri, Dictionary<string, string> fields, Dictionary<string, byte[]> files, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T> PostRequestJson(string requestUri, string jsonContent, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task<T> PostRequestJson(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task PutRequest(string requestUri, T request, CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task PatchRequest(string requestUri, T request, CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
        Task DeleteRequest(string requestUri, CancellationToken token = default(CancellationToken), Dictionary<string, string> additionalHeaders = null);
    }

    public class HttpClientHelper<T> : IHttpClientHelper<T>
    {
        private static HttpClient httpClient;

        public HttpClientHelper(HttpClient _httpClient)
        {
            httpClient = _httpClient;
        }

        public async Task<T> GetSingleItemRequest(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            string requestQuery = string.Empty;
            if (fields != null)
            {
                foreach (var field in fields)
                {
                    if (string.IsNullOrEmpty(requestQuery))
                        requestQuery = "?" + field.Key + "=" + System.Web.HttpUtility.UrlEncode(field.Value); 
                    else requestQuery = requestQuery + "&" + field.Key + "=" + System.Web.HttpUtility.UrlEncode(field.Value);        
                }
            }

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.GetAsync(requestUri + requestQuery, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.ReadAsStringAsync().ContinueWith(x =>
                    {
                        if (typeof(T).Namespace != "System")
                        {
                            result = JsonConvert.DeserializeObject<T>(x.Result);
                        }
                        else result = (T)Convert.ChangeType(x.Result, typeof(T));
                    }, cancellationToken);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T[]> GetMultipleItemsRequest(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T[] result = null;

            if (httpClient == null) return result;

            string requestQuery = string.Empty;
            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(requestQuery))
                    requestQuery = field.Key + "=" + System.Web.HttpUtility.UrlEncode(field.Value); 
                else requestQuery = requestQuery + "&" + field.Key + "=" + System.Web.HttpUtility.UrlEncode(field.Value);        
            }

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.GetAsync(requestUri + "?" + requestQuery, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                    {
                        result = JsonConvert.DeserializeObject<T[]>(x.Result);
                    }, cancellationToken);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T> PostRequest(string requestUri, T request, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.PostAsync(requestUri, new StringContent(JsonConvert.SerializeObject(request)), cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                    {
                        result = JsonConvert.DeserializeObject<T>(x.Result);
                    }, cancellationToken);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T> PostRequest(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            AddHeaders(httpClient, additionalHeaders);
            FormUrlEncodedContent formData = new FormUrlEncodedContent(fields);

            HttpResponseMessage response = await httpClient.PostAsync(requestUri, formData, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    if (typeof(T) == typeof(string))
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = (T)Convert.ChangeType(x.Result.ToString(), typeof(string));
                        }, cancellationToken);
                    }
                    else if (typeof(T) == typeof(byte[]))
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = (T)Convert.ChangeType(x.Result, typeof(byte[]));
                        }, cancellationToken);
                    }
                    else
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = JsonConvert.DeserializeObject<T>(x.Result);
                        }, cancellationToken);
                    }
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T> PostRequest(string requestUri, Dictionary<string, string> fields, Dictionary<string, byte[]> files, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            MultipartFormDataContent formData = new MultipartFormDataContent();

            foreach (KeyValuePair<string, string> field in fields)
                formData.Add(new StringContent(field.Value), field.Key);

            int index = 1;
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                ByteArrayContent fileData = new ByteArrayContent(file.Value);
                formData.Add(fileData, "files_" + index, file.Key);
                index++;
            }

            HttpResponseMessage response = await httpClient.PostAsync(requestUri, formData, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                    {
                        result = JsonConvert.DeserializeObject<T>(x.Result);
                    }, cancellationToken);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T> PostRequestJson(string requestUri, string jsonContent, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.PostAsync(requestUri, new StringContent(jsonContent, Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                    {
                        result = JsonConvert.DeserializeObject<T>(x.Result);
                    }, cancellationToken);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task<T> PostRequestJson(string requestUri, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            T result = default(T);

            if (httpClient == null) return result;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.PostAsync(requestUri, new StringContent(JsonConvert.SerializeObject(fields), Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (response.IsSuccessStatusCode)
                {
                    if (typeof(T) == typeof(string))
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = (T)Convert.ChangeType(x.Result.ToString(), typeof(string));
                        }, cancellationToken);
                    }
                    else if (typeof(T) == typeof(byte[]))
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = (T)Convert.ChangeType(x.Result, typeof(byte[]));
                        }, cancellationToken);
                    }
                    else
                    {
                        await response.Content.ReadAsStringAsync().ContinueWith((Task<string> x) =>
                        {
                            result = JsonConvert.DeserializeObject<T>(x.Result);
                        }, cancellationToken);
                    }
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
            return result;
        }

        public async Task PutRequest(string requestUri, T request, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            if (httpClient == null) return;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.PutAsync(requestUri, new StringContent(JsonConvert.SerializeObject(request)), cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
        }

        public async Task PatchRequest(string requestUri, T request, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            if (httpClient == null) return;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.PatchAsync(requestUri, new StringContent(JsonConvert.SerializeObject(request)), cancellationToken);
            if (response != null)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
        }

        public async Task DeleteRequest(string requestUri, CancellationToken cancellationToken = default(CancellationToken), Dictionary<string, string> additionalHeaders = null)
        {
            if (httpClient == null) return;

            AddHeaders(httpClient, additionalHeaders);
            HttpResponseMessage response = await httpClient.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    response.Content.Dispose();
                    throw new HttpRequestException($"{response.StatusCode}:{content}");
                }
            }
        }

        private void AddHeaders(HttpClient httpClient, Dictionary<string, string> additionalHeaders)
        {
            if (httpClient == null) return;

            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            if (additionalHeaders == null) return;

            foreach (KeyValuePair<string, string> current in additionalHeaders)
            {
                httpClient.DefaultRequestHeaders.Add(current.Key, current.Value);
            }
        }
    }
}