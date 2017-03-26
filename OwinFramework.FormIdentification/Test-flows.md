# OWIN Framework Form Identification Middleware Test Flows

This solution inclides a very primative self-hosted web site that can be 
used to try out the various features of the middleware in this solution.
This test server was used to verify the following user flows though the
forms identification middleware:

> Note that there are many possible ways to exercise the forms identification
> middleware. These flows are the small subset that are tested with each new 
> release. The software is designed so that all possible flows should produce
> reasonable and expected behaviours of the website.

## Flow 1

|  Action  |  Expected result  |
|----------|-------------------|
| User registers for an account with an email address and password, and checks the remember me check box | A new account is created and the user is logged into the site. A welcome email is sent to the registered email address |
| User browses pages of the site | The user's session identifies them as they browse the site |
| The user's session expires | Nothing happens here |
| The user returns to the site with an expired session | The remember me cookie logs them in automatically |
| User browses pages of the site | The user's session identifies them as they browse the site |
| The user logs out from the website | The user's session is flagged as logged out and the remember me cookie is deleted from their browser |
| The user's session expires | Nothing happens here |
| The user returns to the site with an expired session | The user is treated as an anonymous visitor to the site |
| The user attempts to log into the website with a valid email address but the wrong password | The login attempt is denied and the user is still anaonymous |
| The user attempts to log in several more times with the wrong password | The user's account is locked for a period of time |
| The user tries to log in with their email address and valid password | The users account is locked and the login attempt fails |
| The user waits fot the account lock to expire | Nothing happens when the lock expires |
| The user tries to log in with their email address and valid password | The login is successful and the user's session identifies them |
| User browses pages of the site | The user's session identifies them as they browse the site |

## Flow 2

|  Action  |  Expected result  |
|----------|-------------------|
| User registers for an account with an email address and password, and does not check the remember me check box | A new account is created and the user is logged into the site. A welcome email is sent to the registered email address |
| User browses pages of the site | The user's session identifies them as they browse the site |
| The user's session expires | Nothing happens when the session expires |
| The user returns to the site with an expired session | The user is treated as an anonymous visitor to the site |
| The user tries to log in with their email address and valid password | The login is successful and the user's session is initialized with their identity |
| User browses pages of the site | The user's session identifies them as they browse the site |

## Flow 3

|  Action  |  Expected result  |
|----------|-------------------|
| User registers for an account with an email address and password | A new account is created and the user is logged into the site. A welcome email is sent to the registered email address |
| User browses pages of the site | The user's session identifies them as they browse the site, but the email claim has an 'unverfied' status |
| The user clicks the email varification link in the welcome email | The identification sytstem records the verification of the email address |
| User browses pages of the site | The user's session identifies them as they browse the site, and the email claim has a 'verified' status |

## Flow 4

|  Action  |  Expected result  |
|----------|-------------------|
| User logs out from the website | The user's session is flagged as logged out and the remember me cookie is deleted from their browser |
! User provides their email address and requests a password reset email | An email is sent to the user's registered email address |
| User clicks the link in the password reset email | The user lands on a page where they can choose a new password |
| User submits the form with a new password and the remember me check box checked | The user's password is updated and they are logged into the website |
| User browses pages of the site | The user's session identifies them as they browse the site |
| The user's session expires | Nothing happens here |
| The user returns to the site with an expired session | The remember me cookie logs them in automatically |
| User browses pages of the site | The user's session identifies them as they browse the site |
| User clicks the link in the password reset email again | The user lands on a page where they are told that the password reset link has been used already and is no longer valid |
| The user logs out from the website | The user's session is flagged as logged out and the remember me cookie is deleted from their browser |
| The user tries to log in with their email address and their original password | The login fails because the old password is no longer valid |
| The user tries to log in with their email address and their changed password | The login is successful and the user's session identifies them |

## Flow 5

|  Action  |  Expected result  |
|----------|-------------------|
| User registers for an account with an email address and password | A new account is created and the user is logged into the site. A welcome email is sent to the registered email address |
| User browses pages of the site | The user's session identifies them as they browse the site, but the email claim has an 'unverfied' status |
| The submits an email change form with their original email, new email and a valid password | The user remains logged in but their email claim changes to an 'unverified' status. Two emails are sent, one to the ogiginal email address that contains a link to revert the change, the other to the new email address asking the user to verify thier email |
| The user clicks the email varification link in the confirmation email | The identification sytstem records the verification of the email address |
| User browses pages of the site | The user's session identifies them as they browse the site, and the email claim has a 'verified' status |
| User clicks the cancellation link in the email sent to their original email address | The email change is reverted, restoring the previous email address including the verification status |
