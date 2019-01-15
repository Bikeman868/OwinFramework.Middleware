This middleware adds the following behaviour:

* Wraps the downstream OWIN pipeline in a `try/catch` block.
* When exceptions are caught, if they are of type `HttpException` then the Http status code
  and message from the exception are send back to the client as a valid http response.
* For other kinds of execptions the middleware returns either a public apology page or
  detailed technical information. The public apology page is templated and the default template
  allows a message to be inserted. For many applications you will want to change the template
  to match the design of the rest of your site.
* Returns a http status code of 500 (internal server error).
* Optionally sends email with detailed technical information about the exception that occured.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `ExceptionReporterConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

This is an example of adding the exception reporter middleware to the OWIN Framework pipeline builder.

```
builder.Register(ninject.Get<OwinFramework.ExceptionReporter.ExceptionReporterMiddleware>())
    .As("Exception reporter")
    .ConfigureWith(config, "/middleware/exceptions");
```

If you uses the above code, and you use [Urchin](https://github.com/Bikeman868/Urchin) for 
configuration management then you configuration file can be set up like this:

```
{
    "middleware": {
        "exceptions": {
            "message": "Oops, looks like something went wrong",
            "template": "",
			"requiredPermission":"developer",
			"localhost": true,
			"emailAddress":"support@mycompany.com",
			"emailSubject":"Unhandled exception on the web site"
        }
    }
}

```

This configuration specifies that:

* When requests from public visitors cause unhandled execptions in your web site, display the message
  "Oops, looks like something went wrong" using the standard built-in page template.
* If unhandled exceptions occur from a local browser (running on the web server) or if the user
  browsing the page has the 'developer' permission configured in the authorization middleware then
  instead of displaying the public apology message, display detailed technical information to assist
  in tracking down the issue.
* When unhandled exceptions occur, send email with detailed technical information to "support@mycompany.com"
  with the subject of "Unhandled exception on the web site". This will use the standard .Net `SmtpClient` class
  to send the email.
