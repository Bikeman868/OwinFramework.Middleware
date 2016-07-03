# OWIN Framework Middleware

This solution compiles optional NuGet package that enhance the OWIN Framework
by providing useful tools and default implementations of standard middleware.

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
