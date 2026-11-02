using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Rest.Tests
{
    public class TcmfRestSink_Tests
    {
        private static readonly Uri Endpoint = new Uri("https://tcmf.example.org/api/messages");
        private static readonly Uri TokenEndpoint = new Uri("https://idp.example.org/token");

        private static TcmfMessage Message(int n)
        {
            return new TcmfMessage
            {
                MessageId = "tcmf:message:" + Guid.NewGuid(),
                ReportedAt = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc).AddSeconds(n),
                ReportedBy = "tcmf:user:RISE:test",
                Grouping = { "tcmf:grouping:truck:registration_plate:ABC" + n },
                Payload = new LocationState
                {
                    Time = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc).AddSeconds(n),
                    TimeType = TimeType.Actual,
                    TimeSequence = LocationSequence.ArrivedTo,
                    ReferenceObject = "tcmf:reference_object:truck:registration_plate:ABC" + n,
                    Location = "tcmf:location:terminal:SEGOT",
                },
            };
        }

        private static TcmfRestSinkOptions FastOptions()
        {
            return new TcmfRestSinkOptions(Endpoint)
            {
                RetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(5),
                BatchLinger = TimeSpan.FromMilliseconds(10),
            };
        }

        [Test]
        public void Posts_Each_Message_As_Json_With_Headers()
        {
            var handler = new FakeHttpHandler();
            var options = FastOptions();
            options.Headers["X-Api-Key"] = "secret";
            var messages = new[] { Message(1), Message(2), Message(3) };

            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                foreach (var message in messages) sink.Publish(message);
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(3, sink.PublishedCount);
                Assert.AreEqual(3, sink.DeliveredCount);
                Assert.AreEqual(0, sink.FailedCount);
                Assert.AreEqual(0, sink.PendingCount);
            }

            var requests = handler.Requests;
            Assert.AreEqual(3, requests.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(HttpMethod.Post, requests[i].Method);
                Assert.AreEqual(Endpoint, requests[i].Uri);
                Assert.AreEqual("application/json; charset=utf-8", requests[i].ContentType);
                Assert.AreEqual(messages[i].ToJson(), requests[i].Body, "messages are sent in publication order");
                Assert.AreEqual("secret", requests[i].Headers["X-Api-Key"]);
                Assert.IsNull(requests[i].Authorization);
            }
        }

        [Test]
        public void Batches_Messages_Into_A_Json_Array()
        {
            var handler = new FakeHttpHandler();
            var options = FastOptions();
            options.BatchSize = 10;
            options.BatchLinger = TimeSpan.FromMilliseconds(200);

            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                for (int i = 0; i < 5; i++) sink.Publish(Message(i));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(5, sink.DeliveredCount);
            }

            var bodies = handler.Requests.Select(r => r.Body).ToList();
            Assert.That(bodies.Count, Is.LessThanOrEqualTo(5));
            var total = 0;
            foreach (var body in bodies)
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
                    total += doc.RootElement.GetArrayLength();
                    foreach (var element in doc.RootElement.EnumerateArray())
                        Assert.IsTrue(TcmfMessage.FromJson(element.GetRawText()).IsValid);
                }
            }
            Assert.AreEqual(5, total);
        }

        [Test]
        public void Retries_Transient_Failures_Then_Succeeds()
        {
            var handler = new FakeHttpHandler
            {
                Respond = (request, index) => index <= 2
                    ? FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "busy")
                    : FakeHttpHandler.Json(HttpStatusCode.Accepted, ""),
            };

            using (var sink = new TcmfRestSink(FastOptions(), new HttpClient(handler)))
            {
                sink.Publish(Message(1));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, sink.DeliveredCount);
                Assert.AreEqual(3, sink.RequestCount);
            }
        }

        [Test]
        public void Gives_Up_After_MaxRetries_And_Reports_The_Failure()
        {
            var handler = new FakeHttpHandler { Respond = (_, __) => FakeHttpHandler.Json(HttpStatusCode.BadGateway, "down") };
            var options = FastOptions();
            options.MaxRetries = 2;
            var failures = new List<TcmfRestDeliveryFailure>();

            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                sink.OnDeliveryFailed += failures.Add;
                sink.Publish(Message(1));
                var ex = Assert.Throws<TcmfRestDeliveryException>(() => sink.Flush(TimeSpan.FromSeconds(10)));
                Assert.AreEqual(1, ex.Failures.Count);
                Assert.AreEqual(HttpStatusCode.BadGateway, ex.Failures[0].StatusCode);
                Assert.AreEqual("down", ex.Failures[0].ResponseBody);
                Assert.AreEqual(3, ex.Failures[0].Attempts, "1 attempt + 2 retries");
                Assert.AreEqual(1, sink.FailedCount);
                Assert.AreEqual(0, sink.DeliveredCount);
                Assert.AreEqual(1, failures.Count);
                Assert.DoesNotThrow(() => sink.Flush(TimeSpan.FromSeconds(1)), "failures are reported once");
            }
            Assert.AreEqual(3, handler.Requests.Count);
        }

        [Test]
        public void Does_Not_Retry_Client_Errors()
        {
            var handler = new FakeHttpHandler { Respond = (_, __) => FakeHttpHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"schema\"}") };
            using (var sink = new TcmfRestSink(FastOptions(), new HttpClient(handler)))
            {
                sink.Publish(Message(1));
                var ex = Assert.Throws<TcmfRestDeliveryException>(() => sink.Flush(TimeSpan.FromSeconds(10)));
                Assert.AreEqual(1, ex.Failures[0].Attempts);
            }
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [Test]
        public void Later_Messages_Are_Still_Delivered_After_A_Failure()
        {
            var handler = new FakeHttpHandler
            {
                Respond = (request, index) => request.Body.Contains("ABC1")
                    ? FakeHttpHandler.Json(HttpStatusCode.UnprocessableEntity, "no")
                    : FakeHttpHandler.Json(HttpStatusCode.OK, ""),
            };
            var options = FastOptions();
            options.ThrowOnFlushFailure = false;
            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                sink.Publish(Message(1));
                sink.Publish(Message(2));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, sink.FailedCount);
                Assert.AreEqual(1, sink.DeliveredCount);
            }
        }

        [Test]
        public void Honours_Retry_After()
        {
            var handler = new FakeHttpHandler
            {
                Respond = (request, index) =>
                {
                    if (index > 1) return FakeHttpHandler.Json(HttpStatusCode.OK, "");
                    var response = FakeHttpHandler.Json((HttpStatusCode)429, "slow down");
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(300));
                    return response;
                },
            };
            using (var sink = new TcmfRestSink(FastOptions(), new HttpClient(handler)))
            {
                var started = DateTime.UtcNow;
                sink.Publish(Message(1));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.That(DateTime.UtcNow - started, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.AreEqual(2, sink.RequestCount);
            }
        }

        [Test]
        public void Flush_Times_Out_When_The_Endpoint_Hangs()
        {
            var handler = new FakeHttpHandler
            {
                Respond = (_, __) => { Thread.Sleep(2000); return FakeHttpHandler.Json(HttpStatusCode.OK, ""); },
            };
            var options = FastOptions();
            options.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                sink.Publish(Message(1));
                Assert.Throws<TimeoutException>(() => sink.Flush(TimeSpan.FromMilliseconds(200)));
            }
        }

        [Test]
        public void Static_Token_Is_Sent_As_Bearer()
        {
            var handler = new FakeHttpHandler();
            var options = FastOptions();
            options.AccessTokenProvider = new StaticAccessTokenProvider("abc123");
            using (var sink = new TcmfRestSink(options, new HttpClient(handler)))
            {
                sink.Publish(Message(1));
                sink.Flush(TimeSpan.FromSeconds(10));
            }
            Assert.AreEqual("Bearer abc123", handler.Requests[0].Authorization);
        }

        private static FakeHttpHandler OAuthHandler(Func<int, string> tokenForRequest, Func<FakeHttpHandler.Recorded, int, HttpResponseMessage> api = null)
        {
            int tokenRequests = 0;
            var handler = new FakeHttpHandler();
            handler.Respond = (request, index) =>
            {
                if (request.Uri == TokenEndpoint)
                {
                    var n = Interlocked.Increment(ref tokenRequests);
                    return FakeHttpHandler.Json(HttpStatusCode.OK,
                        "{\"access_token\":\"" + tokenForRequest(n) + "\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
                }
                return api != null ? api(request, index) : FakeHttpHandler.Json(HttpStatusCode.OK, "");
            };
            return handler;
        }

        [Test]
        public void OAuth2_Client_Credentials_Token_Is_Requested_Once_And_Reused()
        {
            var handler = OAuthHandler(n => "token-" + n);
            var http = new HttpClient(handler);
            var oauth = new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint,
                ClientId = "sim client",
                ClientSecret = "s3cret",
                Scope = "tcmf:write",
            };
            var options = FastOptions();
            options.AccessTokenProvider = new OAuth2ClientCredentialsTokenProvider(oauth, http);

            using (var sink = new TcmfRestSink(options, http))
            {
                for (int i = 0; i < 3; i++) sink.Publish(Message(i));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(3, sink.DeliveredCount);
            }

            var requests = handler.Requests;
            var tokenRequests = requests.Where(r => r.Uri == TokenEndpoint).ToList();
            var apiRequests = requests.Where(r => r.Uri == Endpoint).ToList();
            Assert.AreEqual(1, tokenRequests.Count);
            Assert.AreEqual(3, apiRequests.Count);

            var token = tokenRequests[0];
            Assert.AreEqual("application/x-www-form-urlencoded", token.ContentType);
            Assert.AreEqual("grant_type=client_credentials&scope=tcmf%3Awrite", token.Body);
            Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("sim%20client:s3cret")), token.Authorization);
            Assert.IsTrue(apiRequests.All(r => r.Authorization == "Bearer token-1"));
        }

        [Test]
        public void OAuth2_Credentials_Can_Go_In_The_Request_Body()
        {
            var handler = OAuthHandler(n => "t");
            var http = new HttpClient(handler);
            var oauth = new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint,
                ClientId = "id",
                ClientSecret = "secret",
                Audience = "https://tcmf.example.org",
                ClientAuthentication = OAuth2ClientAuthentication.RequestBody,
            };
            oauth.AdditionalParameters["resource"] = "api";
            var provider = new OAuth2ClientCredentialsTokenProvider(oauth, http);

            var token = provider.GetAccessTokenAsync(false, CancellationToken.None).Result;

            Assert.AreEqual("t", token);
            var request = handler.Requests.Single();
            Assert.IsNull(request.Authorization);
            Assert.AreEqual("grant_type=client_credentials&audience=https%3A%2F%2Ftcmf.example.org&resource=api&client_id=id&client_secret=secret", request.Body);
        }

        [Test]
        public void OAuth2_Token_Is_Renewed_Before_It_Expires()
        {
            var handler = OAuthHandler(n => "token-" + n);
            var http = new HttpClient(handler);
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var provider = new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint,
                ClientId = "id",
                ClientSecret = "secret",
                ExpiryLeeway = TimeSpan.FromSeconds(60),
            }, http) { Clock = () => now };

            Assert.AreEqual("token-1", provider.GetAccessTokenAsync(false, CancellationToken.None).Result);
            now = now.AddMinutes(58);
            Assert.AreEqual("token-1", provider.GetAccessTokenAsync(false, CancellationToken.None).Result, "still valid, cached");
            now = now.AddMinutes(1.5);
            Assert.AreEqual("token-2", provider.GetAccessTokenAsync(false, CancellationToken.None).Result, "inside the leeway window, renewed");
            Assert.AreEqual("token-3", provider.GetAccessTokenAsync(true, CancellationToken.None).Result, "forced refresh");
            Assert.AreEqual(3, provider.TokenRequests);
        }

        [Test]
        public void OAuth2_Refreshes_Token_And_Retries_Once_On_401()
        {
            var handler = OAuthHandler(n => "token-" + n,
                (request, index) => request.Authorization == "Bearer token-1"
                    ? FakeHttpHandler.Json(HttpStatusCode.Unauthorized, "expired")
                    : FakeHttpHandler.Json(HttpStatusCode.OK, ""));
            var http = new HttpClient(handler);
            var options = FastOptions();
            options.MaxRetries = 0;
            options.AccessTokenProvider = new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint, ClientId = "id", ClientSecret = "secret",
            }, http);

            using (var sink = new TcmfRestSink(options, http))
            {
                sink.Publish(Message(1));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, sink.DeliveredCount);
            }

            var api = handler.Requests.Where(r => r.Uri == Endpoint).Select(r => r.Authorization).ToList();
            CollectionAssert.AreEqual(new[] { "Bearer token-1", "Bearer token-2" }, api);
        }

        [Test]
        public void OAuth2_Persistent_401_Fails_The_Message()
        {
            var handler = OAuthHandler(n => "t" + n, (_, __) => FakeHttpHandler.Json(HttpStatusCode.Unauthorized, "nope"));
            var http = new HttpClient(handler);
            var options = FastOptions();
            options.AccessTokenProvider = new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint, ClientId = "id", ClientSecret = "secret",
            }, http);

            using (var sink = new TcmfRestSink(options, http))
            {
                sink.Publish(Message(1));
                var ex = Assert.Throws<TcmfRestDeliveryException>(() => sink.Flush(TimeSpan.FromSeconds(10)));
                Assert.AreEqual(HttpStatusCode.Unauthorized, ex.Failures[0].StatusCode);
            }
            Assert.AreEqual(2, handler.Requests.Count(r => r.Uri == Endpoint), "one try per token, no further retries");
            Assert.AreEqual(2, handler.Requests.Count(r => r.Uri == TokenEndpoint));
        }

        [Test]
        public void OAuth2_Rejected_Client_Fails_Without_Retrying()
        {
            var handler = new FakeHttpHandler
            {
                Respond = (request, index) => request.Uri == TokenEndpoint
                    ? FakeHttpHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\",\"error_description\":\"bad secret\"}")
                    : FakeHttpHandler.Json(HttpStatusCode.OK, ""),
            };
            var http = new HttpClient(handler);
            var provider = new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint, ClientId = "id", ClientSecret = "wrong",
            }, http);

            var tokenError = Assert.ThrowsAsync<OAuth2TokenException>(() => provider.GetAccessTokenAsync(false, CancellationToken.None));
            StringAssert.Contains("invalid_client - bad secret", tokenError.Message);
            Assert.IsFalse(tokenError.IsTransient);

            var options = FastOptions();
            options.AccessTokenProvider = provider;
            using (var sink = new TcmfRestSink(options, http))
            {
                sink.Publish(Message(1));
                var ex = Assert.Throws<TcmfRestDeliveryException>(() => sink.Flush(TimeSpan.FromSeconds(10)));
                Assert.IsInstanceOf<OAuth2TokenException>(ex.Failures[0].Exception);
                Assert.AreEqual(1, ex.Failures[0].Attempts);
            }
            Assert.AreEqual(0, handler.Requests.Count(r => r.Uri == Endpoint), "no message is sent without a token");
        }

        [Test]
        public void OAuth2_Transient_Token_Endpoint_Error_Is_Retried()
        {
            int tokenCalls = 0;
            var handler = new FakeHttpHandler
            {
                Respond = (request, index) =>
                {
                    if (request.Uri != TokenEndpoint) return FakeHttpHandler.Json(HttpStatusCode.OK, "");
                    return Interlocked.Increment(ref tokenCalls) == 1
                        ? FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "idp restarting")
                        : FakeHttpHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"ok\",\"expires_in\":\"600\"}");
                },
            };
            var http = new HttpClient(handler);
            var options = FastOptions();
            options.AccessTokenProvider = new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions
            {
                TokenEndpoint = TokenEndpoint, ClientId = "id", ClientSecret = "secret",
            }, http);

            using (var sink = new TcmfRestSink(options, http))
            {
                sink.Publish(Message(1));
                sink.Flush(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, sink.DeliveredCount);
            }
            Assert.AreEqual("Bearer ok", handler.Requests.Single(r => r.Uri == Endpoint).Authorization);
        }

        [Test]
        public void Options_Are_Validated()
        {
            Assert.Throws<ArgumentException>(() => new TcmfRestSink(new TcmfRestSinkOptions()));
            Assert.Throws<ArgumentException>(() => new TcmfRestSink(new TcmfRestSinkOptions(Endpoint) { BatchSize = 0 }));
            Assert.Throws<UriFormatException>(() => new TcmfRestSinkOptions("not a url"));
            Assert.Throws<ArgumentException>(() => new OAuth2ClientCredentialsTokenProvider(new OAuth2ClientCredentialsOptions { TokenEndpoint = TokenEndpoint }));
        }

        [Test]
        public void Dispose_Delivers_What_Is_Queued()
        {
            var handler = new FakeHttpHandler();
            var sink = new TcmfRestSink(FastOptions(), new HttpClient(handler));
            for (int i = 0; i < 20; i++) sink.Publish(Message(i));
            sink.Dispose();
            Assert.AreEqual(20, handler.Requests.Count);
            Assert.Throws<ObjectDisposedException>(() => sink.Publish(Message(99)));
        }
    }
}
