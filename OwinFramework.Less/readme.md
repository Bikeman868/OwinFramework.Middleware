This middleware will handle requests for .css files. If there is a physical file with
a .css extension then it will serve this file. If there is no .css file on disk but
there is a .less file with the same name, then it will use dotless to compile the less
file into css and send this as the response.

For this to work efficiently you should configure output caching. Without output caching
the .less file will be compiled on every request.
