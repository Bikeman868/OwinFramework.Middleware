This solution compiles optional NuGet package that enhance the OWIN Framework
by providing useful tools and default implementations of some standard types
of middleware.

These middleware components are designed to get you off the ground, and were
never intended to be full featured. Full featured middleware for things like
identification, authorization, output caching, rendering etc should be
separate projects, and there should be choice and diversity among offerings
from different authors. This is the whole point of creating this open architecture
for middleware interoperability,

The main reason why this project exists is chicken and egg. Developers don't
want to target their packages at this framework if nobody is using it, and 
nobody wants to use the framework unless there is a decent amount of middleware
available.

As well as making this set of middleware available, the framework was deliberately
designed so that middleware developers can support it without requiring
application developers to use it. This is because middleware developers only
have to implement the very simple `IMiddleware<T>` interface for their middleware
to work with the OWIN Framework, and implementing this interface does not
change how the middleware is used for application developers that choose not
to use the OWIN Framework.

Middleware devlopers of the world, please go ahead and implement `IMiddleware<object>`
in your middleware so that application developers can configure it using the Owin
Framework if they choose to.

Each project in this solution has its own readem file with detailed information
about what the middleware provides and how it can be configured. In addition
most of these middleware also impelement the `ISelfDocumenting` interface which
can provide detailed documentation at run-time.

Briefly, the middleware in this solution is:

## AnalysisReporter
The `AnalysisReporter` middleware extracts analytics from middleware that implements `IAnalysable` and formats it into JSON, HTML, plain text etc.

## Dart
The `Dart` middleware provides back-end support for including a UI written in the Dart programming 
language. It does this by detecting if the browser supports Dart and rewriting the
request URL. You will probably want `StaticFiles` middleware to run after this one
unless you plan to generate the content dynamically.

## DefaultDocument
The `DefaultDocument` middleware will rewrite requests for the root of your web site so that they
return a specific document.

## Documenter
The `Documenter` middleware will extract documentation from Middleware that implements `ISelfDocumenting`
and present this in HTML.

## ExceptionReporter
The `ExceptionReporter` middleware will wrap the downstream middleware in a try/catch block and report
exception details. It also allows you to return specific responses by throwing 
`HttpException` in your middleware (for example to permenant redirect).

## Less
The `Less` middleware will handle requests for CSS files. If a file exists with the requested name it
is served as a static file. If a file exists with a .less file extension it will compile
this on the fly into CSS and return this to the browser. It is recommended that you
put the `OutputCache` middleware in front of this one to avoid re-compiling the less
on eac request.

## NotFound
The `NotFound` middleware will always return a 404 response. Put this at the end of your Middleware
pipeline to catch all requests not already handled.

## OutputCache
The `OutputCache` middleware will capture responses to requests and replay these responses from cache
instead of rendering them again through the OWIN pipeline. You have full control over
what is cached and for how long. This improves the performance and scaleability of your
web site.

## RouteVisualizer
The `RouteVisualizer` middleware will generate an SVG visualization of the OWIN pipeline. This lets
you see how your OWIN pipeline is configured.

## StaticFiles
The `StaticFiles` middleware will map paths in the URL to the same relative file path, and if the
file exists will return its contents. You can configure how each file type is handled.
This provides the ability to deploy content in files and have those files requestable
from the browser.

## Versioning
The `Versioning` middleware will modify html before it is returned to the browser, and insert a version
number into the URLs of static assets. When these assets are requested by the browser
this middleware removes the version number from the request URL and adds response
headers telling the browser to cache the asset. This has the effect of transparently
versioning static assets and caching them on the broswer.
