// Copyright (c) 2012-2023 Wojciech Figat. All rights reserved.

#if PLATFORM_IOS

#include "iOSPlatform.h"
#include "iOSWindow.h"
#include "Engine/Core/Log.h"
#include "Engine/Core/Types/String.h"
#include "Engine/Core/Collections/Array.h"
#include "Engine/Platform/Apple/AppleUtils.h"
#include "Engine/Platform/StringUtils.h"
#include "Engine/Platform/MessageBox.h"
#include "Engine/Platform/Window.h"
#include "Engine/Platform/WindowsManager.h"
#include "Engine/Threading/Threading.h"
#include <UIKit/UIKit.h>
#include <sys/utsname.h>

int32 Dpi = 96;
Guid DeviceId;

DialogResult MessageBox::Show(Window* parent, const StringView& text, const StringView& caption, MessageBoxButtons buttons, MessageBoxIcon icon)
{
	NSString* title = (NSString*)AppleUtils::ToString(caption);
	NSString* message = (NSString*)AppleUtils::ToString(text);
	UIAlertController* alert = [UIAlertController alertControllerWithTitle:title message:message preferredStyle:UIAlertControllerStyleAlert];
	UIAlertAction* button = [UIAlertAction actionWithTitle:@"OK" style:UIAlertActionStyleCancel handler:^(id){ }];
	[alert addAction:button];
	ScopeLock lock(WindowsManager::WindowsLocker);
	for (auto window : WindowsManager::Windows)
    {
        if (window->IsVisible())
        {
	        [(UIViewController*)window->GetViewController() presentViewController:alert animated:YES completion:nil];
            break;
        }        
    }
    return DialogResult::OK;
}

bool iOSPlatform::Init()
{
    if (ApplePlatform::Init())
        return true;

    ScreenScale = [[UIScreen mainScreen] scale];
    CustomDpiScale *= ScreenScale;
    Dpi = 72; // TODO: calculate screen dpi (probably hardcoded map for iPhone model)

	// Get device identifier
    NSString* uuid = [UIDevice currentDevice].identifierForVendor.UUIDString;
    String uuidStr = AppleUtils::ToString((CFStringRef)uuid);
    Guid::Parse(uuidStr, DeviceId);

    // TODO: add Gamepad for vibrations usability

    return false;
}

void iOSPlatform::LogInfo()
{
    ApplePlatform::LogInfo();

	struct utsname systemInfo;
	uname(&systemInfo);
    NSOperatingSystemVersion version = [[NSProcessInfo processInfo] operatingSystemVersion];
    LOG(Info, "{3}, iOS {0}.{1}.{2}", version.majorVersion, version.minorVersion, version.patchVersion, String(systemInfo.machine));
}

void iOSPlatform::Tick()
{
    // Process system events
	while (CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.0001, true) == kCFRunLoopRunHandledSource)
    {
    }
}

int32 iOSPlatform::GetDpi()
{
    return Dpi;
}

Guid iOSPlatform::GetUniqueDeviceId()
{
    return DeviceId;
}

String iOSPlatform::GetComputerName()
{
    return TEXT("iPhone");
}

Float2 iOSPlatform::GetDesktopSize()
{
    CGRect frame = [[UIScreen mainScreen] bounds];
	float scale = [[UIScreen mainScreen] scale];
    return Float2((float)frame.size.width * scale, (float)frame.size.height * scale);
}

String iOSPlatform::GetMainDirectory()
{
    String path = StringUtils::GetDirectoryName(GetExecutableFilePath());
    if (path.EndsWith(TEXT("/Contents/iOS")))
    {
        // If running from executable in a package, go up to the Contents
        path = StringUtils::GetDirectoryName(path);
    }
    return path;
}

Window* iOSPlatform::CreateWindow(const CreateWindowSettings& settings)
{
    return New<iOSWindow>(settings);
}

#endif