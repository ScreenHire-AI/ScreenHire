# Recorded model responses

Each file is one consistency check: the source texts it is given, and the HTTP responses Anthropic's Messages API
returns to its calls, in order (claims from each source, then the comparison). `ConsistencyCheckerTests` replays them
through the real `AnthropicClient`, so the SDK's request building and response parsing run as they do in production.

These are hand-written in the Messages API wire format, not captured from a live call: no API key exists in this
project's environment. When a key is available, a live run should replace them, keeping the source texts.

Every person, employer and school in them is fictional.
