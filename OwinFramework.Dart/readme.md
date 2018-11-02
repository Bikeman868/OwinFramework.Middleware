This middleware will serve a UI written in Dart. Dart is a Google language that compiles to 
JavaScript for older browsers that can not run Dart code natively. This middleware detects 
browsers that support Dart natively and serves Dart code to them and compiled JavaScript otherwise.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `DartConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

The default configuration will rewrite request URLs that start with a `/ui` to either `/ui/web`
or `/ui/build/web` depending on whether the browser supports Dart or not. This assumes that you
have other middleware behind this one that can handle the re-written request. For example if you 
place an image called `logo.png` in the `ui\web\images` folder of your site, then you can 
retrieve this file from `http://mydomain/ui/images/logo.png`.

Note that when you compile your Dart application with the `pub` tool it will copy the compiled
version of your Dart code to `\ui\build\web` so everything will work right out of the box.

This is an example configurations that assumes you use [Urchin](https://github.com/Bikeman868/Urchin) 
for as your configuration mechanism.

```
builder.Register(ninject.Get<OwinFramework.Dart.DartMiddleware>())
    .As("Dart")
    .ConfigureWith(config, "/middleware/dart");
```

This is an example Urchin configuration that will work with the code above:

```
{
    "middleware": {
        "dart": {
            "uiRootUrl": "/ui",
			"defaultDocument": "index.html",
            "documentationRootUrl": "/config/dart",
            "dartUiRootUrl": "/ui/web",
            "compiledUiRootUrl": "/ui/build/web",
            "analyticsEnabled": "true"
        }
    }
}
```

This configuration specifies that:

* The url `http://mysite.com/ui` is mapped to the files in the `\ui\web` sub-folder beneath the root folder of 
  the web site for browsers that natively support Dart, and the `\ui\build\web` sub-folder for browsers
  that do not support Dart natively. This is the folder structure that the Dart compiler uses by default.
* The configuration of this middleware can examined by retreieving the url `http://mysite.com/config/dart`.

## Getting Started with Dart

If you want to add a Dart UI to your web site, follow these steps.

1. Download and install the Dart SDK from http://www.dartlang.org/
2. (optional) Download and install the Dartium browser.
3. Create a `ui` folder within your web site.
4. Create a `pubspec.yaml` file in the `ui` folder. See https://www.dartlang.org/tools/pub/get-started
5. Open a command line window and change the working directory to the `ui` folder you just created.
6. Execute the command `pub get`.
7. Create a `web` folder inside the `ui` folder and add a `main.dart` file and an `index.html` file in that folder. 
   Use https://dartpad.dartlang.org/ to figure out whet to put in your first Dart app.
8. From the command line window, make sure you are in the `ui` folder, then execute the 
   command `pub build`. This will create a `ui\build\web` folder containing the compiled
   JavaScript for your Dart app.
9. Add `DartMiddleware` to your OWIN pipeline as described above. The default configuration will work
   if you followed the steps above.
10. Open the `/ui` path of your web site in Dartium and other browsers. Use the developer tools built into the browser
    to see which ones run native Dart code and which ones run the compiled JavaScript.

## Use with other middleware

This middleware needs something behind it to serve the contents of the files. You can 
add the `StaticFiles` middleware for this, or any other middleware that will respond 
with html, css etc.

You can optionally add the `Less` middleware to the ppeline. This will allow you to write
your styles in Less instead of CSS.

You can optionally add the `Versioning` middleware to make your website more efficient.

Note that if you add the `OutputCache` middleware it will not cache the output because
the exact same URL produces different results for different browsers, and caching the
output would mess this up.
