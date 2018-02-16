#pragma once

namespace GvLib
{
    /// <summary>
    /// Class that represents a relative path (and its descendants) and the notification bit mask
    /// that should apply to them.
    ///
    /// StartVirtualizationInstanceEx takes a collection of these to configure desired notification callbacks.
    ///
    /// Rules:
    ///
    ///   The notificationMask would apply to the path and all its descendants (if the path is a directory) unless a
    ///   descendant has a mapping of it's own (i.e nested notification mapping, wherein it's own mapping will apply) 
    ///
    ///   If nested notification mappings are desired, they must be set up top-to-bottom 
    ///   (e.g. "C:\Windows" has to be set up before "C:\Windows\System32"). 
    ///
    ///   Note that all NotificationMappings are relative to virtualization root. Hence an empty string refers to the Virtualization Root.
    ///   Thus if C:\Windows is virtualization root, the notification root would be "". Similarly 
    ///   C:\Windows\System32\drivers would be "System32\drivers".
    ///
    ///   Since notification roots are to be provided in a top down fashion, only the first notification mapping 
    ///   entry could be an empty notification root. 
    ///
    ///   NotificationType::None can be used to prevent a path (and its descendants) from receiving notifications.
    /// </summary>
    public ref class NotificationMapping
    {
    public:
        NotificationMapping();
        NotificationMapping(NotificationType notificationMask, System::String^ notificationRoot);

        /// <summary>
        /// Notification mask to apply at NotificationRoot and its descendants
        /// </summary>
        property NotificationType NotificationMask
        {
            NotificationType get(void);
            void set(NotificationType mask);
        }

        /// <summary>
        /// Path at which to apply the NotificationMask, relative to the virtualization root
        /// </summary>
        property System::String^ NotificationRoot
        {
            System::String^ get(void);
            void set(System::String^ root);
        }

    private:
        NotificationType notificationMask;
        System::String^ notificationRoot;
    };

    inline NotificationMapping::NotificationMapping()
        : notificationMask(NotificationType::None)
        , notificationRoot(nullptr)
    {
    }

    inline NotificationMapping::NotificationMapping(NotificationType notificationMask, System::String^ notificationRoot)
        : notificationMask(notificationMask)
        , notificationRoot(notificationRoot)
    {
    }

    inline NotificationType NotificationMapping::NotificationMask::get(void)
    {
        return this->notificationMask;
    }

    inline void NotificationMapping::NotificationMask::set(NotificationType mask)
    {
        this->notificationMask = mask;
    }

    inline System::String^ NotificationMapping::NotificationRoot::get(void)
    {
        return this->notificationRoot;
    }

    inline void NotificationMapping::NotificationRoot::set(System::String^ root)
    {
        this->notificationRoot = root;
    }
}
