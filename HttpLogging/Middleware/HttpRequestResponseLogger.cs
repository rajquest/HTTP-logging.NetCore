using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HttpLogging.Middleware
{
    public class HttpRequestResponseLogger
    {
        RequestDelegate next;

        public HttpRequestResponseLogger(RequestDelegate next)
        {
            this.next = next;
        }

        //can not inject as a constructor parameter in Middleware because only Singleton services can be resolved
        //by constructor injection in Middleware. Moved the dependency to the Invoke method
        public async Task InvokeAsync(HttpContext context)
        {
            // HttpLog will contain response and request related data
            HttpLog logEntry = new HttpLog();
            await RequestLogger(context, logEntry);

            await next.Invoke(context);

            await ResponseLogger(context, logEntry);

            //store log to database repository
            //repoLogs.SaveLog(logEntry);
        }

        // Handle web request values
        public async Task RequestLogger(HttpContext context, HttpLog log)
        {
            string requestHeaders = string.Empty;

            log.RequestedOn = DateTime.Now;
            log.Method = context.Request.Method;
            log.Path = context.Request.Path;
            log.QueryString = context.Request.QueryString.ToString();
            log.ContentType = context.Request.ContentType;

            foreach (var headerDictionary in context.Request.Headers)
            {
                //ignore secrets and unnecessary header values
                if (headerDictionary.Key != "Authorization" && headerDictionary.Key != "Connection" &&
                    headerDictionary.Key != "User-Agent" && headerDictionary.Key != "Postman-Token" &&
                    headerDictionary.Key != "Accept-Encoding")
                {
                    requestHeaders += headerDictionary.Key + "=" + headerDictionary.Value + ", ";
                }
            }

            if (requestHeaders != string.Empty)
                log.Headers = requestHeaders;

            //Request handling. Check if the Request is a POST call 
            if (context.Request.Method == "POST")
            {
                context.Request.EnableBuffering();
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                context.Request.Body.Position = 0;
                log.Payload = body;
            }
        }

        //handle response values
        public async Task ResponseLogger(HttpContext context, HttpLog log)
        {
            using (Stream originalRequest = context.Response.Body)
            {
                try
                {
                    using (var memStream = new MemoryStream())
                    {
                        context.Response.Body = memStream;
                        // All the Request processing as described above 
                        // happens from here.
                        // Response handling starts from here
                        // set the pointer to the beginning of the 
                        // memory stream to read
                        memStream.Position = 0;
                        // read the memory stream till the end
                        var response = await new StreamReader(memStream)
                            .ReadToEndAsync();
                        // write the response to the log object
                        log.Response = response;
                        log.ResponseCode = context.Response.StatusCode.ToString();
                        log.IsSuccessStatusCode = (
                            context.Response.StatusCode == 200 ||
                            context.Response.StatusCode == 201);
                        log.RespondedOn = DateTime.Now;

                        // since we have read till the end of the stream, 
                        // reset it onto the first position
                        memStream.Position = 0;

                        // now copy the content of the temporary memory 
                        // stream we have passed to the actual response body 
                        // which will carry the response out.
                        await memStream.CopyToAsync(originalRequest);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                finally
                {
                    // assign the response body to the actual context
                    context.Response.Body = originalRequest;
                }
            }
        }
    }
}