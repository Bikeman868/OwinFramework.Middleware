import 'dart:html';

main()
{ 
	InputElement version = querySelector('#version');
	Element div = querySelector('#dart');

	div.innerHtml = r'<p>If you can see this then Dart is running.</p>'
		+ '<p>The version suffix is ' + version.value + '</p>';
}
