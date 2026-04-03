namespace HgermanContentFactory.Core.Enums;

public enum ContentLanguage
{
    English = 1,
    German  = 2,
    Spanish = 3,
    French  = 4,
    Italian = 5,
    Polish  = 6
}

public enum NicheCategory
{
    Technology    = 1,
    Finance       = 2,
    Health        = 3,
    Lifestyle     = 4,
    Education     = 5,
    Entertainment = 6,
    Travel        = 7,
    Food          = 8,
    Sports        = 9,
    Science       = 10,
    Business      = 11,
    Gaming        = 12,
    Fashion       = 13,
    DIY           = 14,
    Nature        = 15,
    TurkishDrama = 16
}

public enum VideoStatus
{
    Pending        = 0,
    ScriptReady    = 1,
    MediaReady     = 2,
    Rendered       = 3,
    Published      = 4,
    Failed         = 5,
    Scheduled      = 6,
    Cancelled      = 7
}

public enum PublishPlatform
{
    YouTube   = 1,
    Instagram = 2,
    TikTok    = 3
}

public enum TrendStatus
{
    Rising   = 1,
    Peak     = 2,
    Declining= 3,
    Stable   = 4
}

public enum ScheduleFrequency
{
    Daily  = 1,
    Weekly = 2,
    Custom = 3
}
