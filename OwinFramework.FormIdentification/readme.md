# OWIN Framework Form Identification Middleware

This middleware allows users of your site to:

* Create an account using an email address and a password by making an HTTPS POST
request to the URL that you have configured (defaults to `/formId/signup`). On success
redirects the user to a welcome page whose URL you can configure (defaults to `/welcome`).
On failure redirects the user to a failed/retry page whose URL you can configure
(defaults to `/formId/signup`). Your application is responsible for delivering the UI, this
middleware just accepts the POST, creates the account or not, and performs a redriection.

* Log into an existing account using the email address and password used to create the
account. The email and password must be sent in an HTTPS POST to a URL that you can
configure (defaults to `/formId/signin`). The POST will result in a redirect response. The URLs 
for the success and fail cases can both be configured. Your application is responsible for 
handling the HTTP(S) GET requests for these URLs.

* Change their password by providing their current password and a new one.

* Logout, securing their account.

* Request a time-limited password reset email to be sent.

* Click the single use link in the password reset email and reset their password
without knowing their current password.

Note that there are other middleware packages that provide user identification from
social media, shared secrets and certificates. These middleware packages will all work 
togther because each one will look to see if the user is already identified before
trying to perform its own identification. The first middleware in the pipeline that
successfully identifies the user will supply the user identity to the reast of the
pipeline and other identification middleware will not make any additional user
identification attempts after the user is identified.

Note that this middleware will store a cookie on the browser to indicate who the user
is logged in as. The lifetime of the cookie is configurable and defaults to 90 days.
To keep this cookie secure this middleware performs a few extra redirections that mean
that the cookie can be stored on the browser in a different domain than the non secure 
pages of the site are served from.

When a browser makes a request to a server it includes all of the cookies that are stored
for that domain (for example `www.mycompany.com`). If the login request set cookies in
the `www.mycompany.com` domain then every time the browser requested any page in the 
`www.mycompany.com` domain it would send the cookie, and if any of these requests are not
encrypted (even requets for images or stylesheets etc) then anyone watching could copy
the cookie value and use it to impersonate that user for 90 days.

For this reason this middleware assumes that you will use a sub-domain for log in etc
and store cookies in this sub-domain on the browser so that they will not be sent with
non secure page requests. You don't have to do this and it will still work, but you
must then make sure all requests to your site are secure (using HTTPS) and if a user
types HTTP into their browser their user account will be at risk even if your server
does not handle the request.

Lets assume that your main site domain is `http://www.mycompany.com/` and you choose to
use `https://secure.mycompany.com/` for login, this is what happens when a user visits
your site:

1. The very first time they visit `http://www.mycompany.com/` this middleware will notice 
that there is no login token in session and will redirect the browser to a page on the
secure sub-domain, for example https://secure.mycompany.com/renewSession` since this
is their first visit and no cookies have been set, tihs will store a value in session
indicating that they are an anonymous user and redirect them back to the page that
they initially requested. Now when the browser re-requests  `http://www.mycompany.com/` 
this middleware will find the anonymous token in session and serve the page as an
unidentified user.

2. For subsequent requests during the session, requests will continue to be processed as
an unidentified user. When the session times out, the process will revert back to step 1.

3. If the user decides to sign in by POSTing to `https://secure.mycompany.com/signin` then
this middleware will check their credentials and respond with a page containing a
cookie that identifies the user and JavaScript to load `https://secure.mycompany.com/renewSession`.
This will store the user identification cookie on the browser in the `secure.mycompany.com`
domain and load `https://secure.mycompany.com/renewSession`.

4. When the browser makes a request to `https://secure.mycompany.com/renewSession` is will
include the cookies for the `secure.mycompany.com` domain, including the user identification
cookie. This middleware will handle this request by putting information into the users
session about their identity, and redirecting back to the page they first came from on
`http://www.mycompany.com/`. 

5. When this middleware receives a request for `http://www.mycompany.com/` it will see the
user's identity in their session and process the request as that identified user. Note that
the browser does not send the user identification cookie because this is for a different
domain (`www.mycompany.com` instead of `secure.mycompany.com`).

6. If the user's session expires whilst they are logged on, then this middleware will redirect
the browser to  `https://secure.mycompany.com/renewSession`. When the browser makes this
request, because it is for the `secure.mycompany.com` domain the user identification cookie
will be included in the request. This middleware will handle this request by storing the
user identity in the new session just like in step 4.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

This middleware has very few configuration options. See the `FormIdentificationConfiguration.cs`
for details.

This is an example of adding the form identification middleware to the OWIN Framework pipeline builder.

```
builder.Register(ninject.Get<OwinFramework.FormIdentification.FormIdentificationMiddleware>())
    .As("Form identification")
    .ConfigureWith(config, "/middleware/formIdentification");
```

If you use the above code, and you use [Urchin](https://github.com/Bikeman868/Urchin) for 
configuration management then your configuration file can be set up like this:

```
{
    "middleware": {
        "formIdentification": {
            "secureDomain": "secure.mycompany.com",
			"documentationPage": "/formId/config",
			"signupPage": "/formId/signup",
			"signinPage": "/formId/signin",
			"signoutPage": "/formId/signout",
			"signupSuccessPage": "/welcome",
			"signupFailPage": "/formId/signup",
			"signinSuccesPage": "/",
			"signinFailPage": "/formId/signin",
			"sendPasswordResetPage": "/formId/sendPasswordReset",
			"resetPasswordPage": "/formId/resetPassword",
			"renewSessionPage": "/formsId/renew",
			"cookieName": "forms_user_identification",
			"sessionName": "forms_user_identification",
        }
    }
}

```

## Templates

This middleware contains templates embedded into the DLL. These templates are described
in more detail below. When the middleware needs one of these templates it will first
look in the main web site folder for a file with that name, and if none is found will
revert back to the embedded templates inside the DLL.

To customize these templates you just have to deploy a file into the root folder of
your site. The template files are:

### PasswordResetEmail.txt

This is a template for the plain text version of the email that gets sent out when users 
request a password reset. You should customize this template to make it more specific to 
your website. When customizing this template, start from the built in one which is in the
`html` folder in the source code.

### PasswordResetEmail.txt

This is a template for the html version of the email that gets sent out when users 
request a password reset. You should customize this template to make it more specific to 
your website. When customizing this template, start from the built in one which is in the
`html` folder in the source code.

### configuration.html

This is the template for a page that describes the configuration options for this
middleware. It is unlikely that you will want to override this template.

### description.html

This template allows the middleware to be self-documenting. It is unlikely that you 
will want to override this template.

### redirect.html

This template is used for redirections from the secure to the non-secure pages where
a cookie must be set before redirecting to the other domain. It is unlikely that you 
will want to override this template.