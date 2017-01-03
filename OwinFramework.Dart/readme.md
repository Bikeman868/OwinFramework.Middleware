# OWIN Framework Dart Middleware

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

The default configuration will serve a Dart application from a `ui` folder in your web site via urls
starting with `/ui`. For example if you place an image called `logo.png` in the `ui\images`
folder of your site, then you can retrieve this file from `http://mydomain/ui/images/logo.png`.

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
            "dartUiRootUrl": "/ui",
			"defaultDocument": "index.html",
            "documentationRootUrl": "/config/dart",
            "rootDartDirectory": "~\\ui\\web",
            "rootBuildDirectory": "~\\ui\\build\\web",
            "enabled": "true",
            "maximumFileSizeToCache": 10000,
            "totalCacheSize": 1000000,
            "maximumCacheTime": "00:30:00",
            "requiredPermission": "",
			"version": 1
        }
    }
}
```

This configuration specifies that:

* The url `http://mysite/ui` is mapped to the files in the `\ui\web` sub-folder beneath the root folder of 
  the web site for browsers that natively support Dart, and the `\ui\build\web` sub-folder for browsers
  that do not support Dart natively. This is the folder structure that the Dart compiler uses by default.

* The configuration of this middleware can examined by retreieving the url `http://mysite/config/dart`.

* All static files are cached in memory for 30 minutes if they are less than 10,000 bytes in size up to a 
maximum total memory consumption of 1,000,000 bytes for all files. This featre relies on
output caching middleware. If there is no output caching middleware configured in your OWIN pipeline
then these files will not be cached.

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

## Versioning

To make your web site more efficient, this middleware will add headers to all responses telling
the browser to cache the response. This will result in a more responsive site and lower load on
your servers, but what happens when I changed some files and I want the browser to get a fresh
copy rather than using the cached one?

This middleware can modify all the HTML that it serves to replace {_v_} with a version number. You
can use this to add a version number to any asset that should be cached by the browser. The version
number must be placed immediately before the file extension.

For example instead of writing this in the head of your html

```
    <link rel="stylesheet" href="/ui/styles.css" />

```

You can write this

```
    <link rel="stylesheet" href="/ui/styles{_v_}.css" />

```

Which will get translated into this on its way to the browser

```
    <link rel="stylesheet" href="/ui/styles_v1.css" />

```

When this middleware receives requests for static files, it will accept requests with and without the version 
number, and it will still serve the same file, so `http://mysite/ui/styles.css` and `http://mysite/ui/styles_v1.css`
will return the same file.

If you make a request for the wrong version number, then this middleware will not handle the request, and pass it
down the pipeline. In most configurations you would want this to be handled by middleware that returns a 404 response.

Although  `http://mysite/ui/styles.css` and `http://mysite/ui/styles_v1.css` return the contents of the same file, the
cache headers that are returned are different. The browser will be instructed to cache the versioned asset but not
cache the unversioned one.

The current version number is configured in the Urchin configuration. If you deploy a new version of your application
you should increment the version number so that all the browsers will fetch the updated assets. You can also set the
version number to `null` or delete it from your configuration to disable the versioning of assets. In this case
assets will not be versioned, and will therefore not be cached. This can be useful during debugging, but is rarely
the best configuration for a production deployment.
