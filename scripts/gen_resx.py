"""One-shot generator for the HAMMOR string resources.

Writes Strings.resx (English, neutral) and Strings.ar.resx (Arabic).
After this runs, the .resx files are the source of truth and should be edited
directly -- this script exists only to create them with correct UTF-8 Arabic
and a valid resx header in one pass.
"""

import xml.etree.ElementTree as ET
from pathlib import Path

# key -> (english, arabic)
STRINGS: dict[str, tuple[str, str]] = {
    # --- Application / shell ---
    "App.Title": ("HAMMOR", "هامور"),
    "App.Tagline": ("Personal AI assistant", "مساعد ذكي شخصي"),

    # --- Navigation ---
    "Nav.Chat": ("Chat", "المحادثة"),
    "Nav.Activity": ("Activity", "النشاط"),
    "Nav.Tasks": ("Tasks", "المهام"),
    "Nav.Memory": ("Memory", "الذاكرة"),
    "Nav.Projects": ("Projects", "المشاريع"),
    "Nav.Settings": ("Settings", "الإعدادات"),

    # --- Chat ---
    "Chat.Title": ("Chat", "المحادثة"),
    "Chat.InputPlaceholder": ("Ask HAMMOR anything…", "اسأل هامور عن أي شيء…"),
    "Chat.Send": ("Send", "إرسال"),
    "Chat.Speak": ("Speak reply", "نطق الرد"),
    "Chat.StopSpeaking": ("Stop speaking", "إيقاف النطق"),
    "Chat.Working": ("HAMMOR is working…", "هامور يعمل…"),
    "Chat.EmptyTitle": ("Start a conversation", "ابدأ محادثة"),
    "Chat.EmptyBody": (
        "Type a message below. HAMMOR replies in the language you write in.",
        "اكتب رسالة في الأسفل. يرد هامور باللغة التي تكتب بها.",
    ),
    "Chat.You": ("You", "أنت"),
    "Chat.Clear": ("Clear conversation", "مسح المحادثة"),
    "Chat.VoiceInputUnavailable": (
        "Voice input is not available: speech-to-text is not implemented yet.",
        "الإدخال الصوتي غير متاح: لم يتم تنفيذ تحويل الكلام إلى نص بعد.",
    ),

    # --- Status ---
    "Status.Ready": ("Ready", "جاهز"),
    "Status.Busy": ("Busy", "مشغول"),
    "Status.Claude": ("Claude", "كلود"),
    "Status.ElevenLabs": ("ElevenLabs", "إيليفن لابس"),
    "Status.SpeechToText": ("Speech-to-text", "تحويل الكلام إلى نص"),
    "Status.Microphone": ("Microphone", "الميكروفون"),
    "Status.Speaker": ("Speaker", "مكبر الصوت"),
    "Status.BackgroundTasks": ("Background tasks", "المهام الخلفية"),
    "Status.Available": ("Available", "متاح"),
    "Status.Unavailable": ("Unavailable", "غير متاح"),
    "Status.Connected": ("Connected", "متصل"),
    "Status.Error": ("Error", "خطأ"),
    "Status.Refresh": ("Refresh status", "تحديث الحالة"),

    # --- Settings: general ---
    "Settings.Title": ("Settings", "الإعدادات"),
    "Settings.General": ("General", "عام"),
    "Settings.Language": ("Language", "اللغة"),
    "Settings.LanguageDescription": (
        "Changes apply immediately, with no restart.",
        "يتم تطبيق التغييرات فورًا بدون إعادة تشغيل.",
    ),
    "Settings.Theme": ("Theme", "المظهر"),
    "Settings.Theme.System": ("Use system setting", "حسب إعداد النظام"),
    "Settings.Theme.Light": ("Light", "فاتح"),
    "Settings.Theme.Dark": ("Dark", "داكن"),
    "Settings.LaunchOnStartup": ("Launch HAMMOR when Windows starts", "تشغيل هامور مع بدء ويندوز"),
    "Settings.StartMinimised": ("Start minimised to the system tray", "البدء مصغرًا في شريط المهام"),

    # --- Settings: AI ---
    "Settings.Ai": ("AI provider", "مزود الذكاء الاصطناعي"),
    "Settings.Ai.Provider": ("Primary provider", "المزود الأساسي"),
    "Settings.Ai.Model": ("Model", "النموذج"),
    "Settings.Ai.ApiKey": ("Anthropic API key", "مفتاح واجهة Anthropic"),
    "Settings.Ai.MaxTokens": ("Maximum output tokens", "الحد الأقصى لرموز الإخراج"),
    "Settings.Ai.Effort": ("Reasoning effort", "مستوى التفكير"),

    # --- Settings: voice ---
    "Settings.Voice": ("Voice", "الصوت"),
    "Settings.Voice.TtsProvider": ("Text-to-speech provider", "مزود تحويل النص إلى كلام"),
    "Settings.Voice.SttProvider": ("Speech-to-text provider", "مزود تحويل الكلام إلى نص"),
    "Settings.Voice.VoiceId": ("ElevenLabs voice ID", "معرّف صوت ElevenLabs"),
    "Settings.Voice.VoiceIdDescription": (
        "The voice HAMMOR speaks with. Change it here; it is not fixed in code.",
        "الصوت الذي يتحدث به هامور. يمكن تغييره من هنا، وهو غير مثبّت في الكود.",
    ),
    "Settings.Voice.ApiKey": ("ElevenLabs API key", "مفتاح واجهة ElevenLabs"),
    "Settings.Voice.SpeakAutomatically": ("Speak replies automatically", "نطق الردود تلقائيًا"),
    "Settings.Voice.OutputDevice": ("Output device", "جهاز الإخراج"),
    "Settings.Voice.InputDevice": ("Input device", "جهاز الإدخال"),
    "Settings.Voice.SystemDefault": ("System default", "افتراضي النظام"),
    "Settings.Voice.Volume": ("Output volume", "مستوى الصوت"),
    "Settings.Voice.TestSpeech": ("Test speech", "اختبار النطق"),
    "Settings.Voice.TestPhrase": ("HAMMOR is ready.", "هامور جاهز."),
    "Settings.Voice.NoStreaming": (
        "This provider buffers the full reply before playback. Streaming is not implemented.",
        "يقوم هذا المزود بتحميل الرد بالكامل قبل التشغيل. البث المباشر غير مُنفَّذ.",
    ),

    # --- Settings: memory ---
    "Settings.Memory": ("Memory", "الذاكرة"),
    "Settings.Memory.Location": ("Storage location", "موقع التخزين"),
    "Settings.Memory.IndexOnStartup": ("Index markdown memory on startup", "فهرسة ذاكرة ماركداون عند البدء"),
    "Settings.Memory.Reindex": ("Re-index now", "إعادة الفهرسة الآن"),

    # --- Settings: security ---
    "Settings.Security": ("Security", "الأمان"),
    "Settings.Security.AutoApprove": ("Auto-approve tools up to", "الموافقة التلقائية على الأدوات حتى"),
    "Settings.Security.AutoApproveDescription": (
        "Tools above this level always ask before running.",
        "الأدوات الأعلى من هذا المستوى تطلب الإذن دائمًا قبل التشغيل.",
    ),
    "Settings.Security.AlwaysConfirmDestructive": (
        "Always confirm destructive actions",
        "تأكيد الإجراءات المدمّرة دائمًا",
    ),
    "Settings.Security.DestructiveLocked": (
        "Destructive actions always require confirmation and cannot be auto-approved.",
        "تتطلب الإجراءات المدمّرة تأكيدًا دائمًا ولا يمكن الموافقة عليها تلقائيًا.",
    ),

    # --- Settings: actions / misc ---
    "Settings.Save": ("Save changes", "حفظ التغييرات"),
    "Settings.Saved": ("Settings saved.", "تم حفظ الإعدادات."),
    "Settings.TestConnection": ("Test connection", "اختبار الاتصال"),
    "Settings.ApiKeyStored": ("A key is stored.", "تم تخزين مفتاح."),
    "Settings.ApiKeyNotStored": ("No key stored.", "لا يوجد مفتاح مخزَّن."),
    "Settings.ApiKeyPlaceholder": ("Paste a key to replace the stored one", "الصق مفتاحًا لاستبدال المخزَّن"),
    "Settings.SaveKey": ("Save key", "حفظ المفتاح"),
    "Settings.ClearKey": ("Remove key", "إزالة المفتاح"),
    "Settings.SecretsNote": (
        "Keys are encrypted with Windows DPAPI for your user account. They are never "
        "written to the configuration file or to logs.",
        "تُشفَّر المفاتيح باستخدام Windows DPAPI لحسابك. ولا تُكتب أبدًا في ملف "
        "الإعدادات أو في السجلات.",
    ),
    "Settings.ConfigFile": ("Configuration file", "ملف الإعدادات"),

    # --- Tasks ---
    "Tasks.Title": ("Tasks", "المهام"),
    "Tasks.Empty": ("No tasks yet.", "لا توجد مهام بعد."),
    "Tasks.State.Pending": ("Pending", "قيد الانتظار"),
    "Tasks.State.Running": ("Running", "قيد التنفيذ"),
    "Tasks.State.Completed": ("Completed", "مكتملة"),
    "Tasks.State.Failed": ("Failed", "فاشلة"),
    "Tasks.State.Cancelled": ("Cancelled", "ملغاة"),
    "Tasks.SchedulerNote": (
        "Background scheduling and retry are not implemented yet. Tasks recorded here "
        "persist and are reconciled after a crash, but nothing runs them automatically.",
        "لم يتم تنفيذ الجدولة الخلفية وإعادة المحاولة بعد. المهام المسجلة هنا تُحفظ "
        "ويتم تسويتها بعد أي تعطّل، لكن لا شيء يُشغّلها تلقائيًا.",
    ),

    # --- Memory ---
    "Memory.Title": ("Memory", "الذاكرة"),
    "Memory.SearchPlaceholder": ("Search memory…", "ابحث في الذاكرة…"),
    "Memory.Search": ("Search", "بحث"),
    "Memory.Empty": ("No memory entries yet.", "لا توجد مدخلات ذاكرة بعد."),
    "Memory.NoResults": ("Nothing matched your search.", "لا توجد نتائج مطابقة."),
    "Memory.SemanticUnavailable": (
        "Semantic search is not implemented. This searches by keyword.",
        "البحث الدلالي غير مُنفَّذ. هذا بحث بالكلمات المفتاحية.",
    ),
    "Memory.OpenFolder": ("Open memory folder", "فتح مجلد الذاكرة"),

    # --- Projects ---
    "Projects.Title": ("Projects", "المشاريع"),
    "Projects.Empty": ("No projects yet.", "لا توجد مشاريع بعد."),
    "Projects.Add": ("Add project", "إضافة مشروع"),
    "Projects.NotImplemented": (
        "Project creation and git inspection are not implemented yet. The project store "
        "and data model exist and are used to scope memory and tasks.",
        "لم يتم تنفيذ إنشاء المشاريع وفحص git بعد. مخزن المشاريع ونموذج البيانات "
        "موجودان ويُستخدمان لتحديد نطاق الذاكرة والمهام.",
    ),

    # --- Activity ---
    "Activity.Title": ("Activity", "النشاط"),
    "Activity.Empty": ("No recorded activity yet.", "لا يوجد نشاط مسجَّل بعد."),
    "Activity.Description": (
        "Audit trail of authorisations, tool runs and provider errors.",
        "سجل تدقيق للتصاريح وتشغيل الأدوات وأخطاء المزودين.",
    ),
    "Activity.Outcome.Allowed": ("Allowed", "مسموح"),
    "Activity.Outcome.Denied": ("Denied", "مرفوض"),
    "Activity.Outcome.Succeeded": ("Succeeded", "نجح"),
    "Activity.Outcome.Failed": ("Failed", "فشل"),
    "Activity.Outcome.Information": ("Information", "معلومة"),

    # --- Confirmation dialog ---
    "Confirm.Title": ("Permission required", "مطلوب إذن"),
    "Confirm.Allow": ("Allow", "السماح"),
    "Confirm.Deny": ("Deny", "رفض"),
    "Confirm.Tool": ("Tool", "الأداة"),
    "Confirm.Permission": ("Permission level", "مستوى الإذن"),
    "Confirm.Details": ("Arguments", "الوسائط"),

    # --- Permission levels ---
    "Permission.Read": ("Read", "قراءة"),
    "Permission.Write": ("Write", "كتابة"),
    "Permission.Execute": ("Execute", "تنفيذ"),
    "Permission.Destructive": ("Destructive", "مدمّر"),

    # --- First-run setup ---
    "Setup.Title": ("Welcome to HAMMOR", "مرحبًا بك في هامور"),
    "Setup.ChooseLanguage": ("Choose your language", "اختر لغتك"),
    "Setup.Step": ("Step", "خطوة"),
    "Setup.Next": ("Next", "التالي"),
    "Setup.Back": ("Back", "رجوع"),
    "Setup.Finish": ("Finish setup", "إكمال الإعداد"),
    "Setup.Skip": ("Skip for now", "تخطَّ الآن"),
    "Setup.AiStep": ("Connect Claude", "ربط كلود"),
    "Setup.VoiceStep": ("Set up voice", "إعداد الصوت"),
    "Setup.MemoryStep": ("Choose where data is stored", "اختر مكان تخزين البيانات"),
    "Setup.ReadyTitle": ("Setup complete", "تم الإعداد"),
    "Setup.ReadyMessage": ("HAMMOR is ready.", "هامور جاهز."),
    "Setup.OptionalNote": (
        "You can skip anything here and configure it later in Settings.",
        "يمكنك تخطي أي شيء هنا وإعداده لاحقًا من الإعدادات.",
    ),
    "Setup.ClaudeCodeDetected": ("Claude Code CLI detected", "تم العثور على Claude Code CLI"),
    "Setup.ClaudeCodeNotDetected": ("Claude Code CLI not found", "لم يتم العثور على Claude Code CLI"),

    # --- Tray / hotkey ---
    "Tray.Open": ("Open HAMMOR", "فتح هامور"),
    "Tray.Exit": ("Exit", "إنهاء"),
    "Tray.NotImplemented": (
        "System tray, global hotkey and the quick input overlay are not implemented yet.",
        "لم يتم تنفيذ شريط المهام والاختصار العام ونافذة الإدخال السريع بعد.",
    ),

    # --- Common ---
    "Common.NotConfigured": ("Not Configured", "غير مُهيَّأ"),
    "Common.NotImplemented": ("Not Implemented", "غير مُنفَّذ"),
    "Common.Cancel": ("Cancel", "إلغاء"),
    "Common.Close": ("Close", "إغلاق"),
    "Common.Ok": ("OK", "موافق"),
    "Common.Browse": ("Browse…", "استعراض…"),
    "Common.Copy": ("Copy", "نسخ"),
    "Common.Error": ("Something went wrong", "حدث خطأ ما"),
}

RESX_HEADER_SCHEMA = """
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
"""

RESHEADERS = [
    ("resmimetype", "text/microsoft-resx"),
    ("version", "2.0"),
    ("reader", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
    ("writer", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
]


def build(index: int) -> str:
    parts = ['<?xml version="1.0" encoding="utf-8"?>', "<root>", RESX_HEADER_SCHEMA.rstrip()]

    for name, value in RESHEADERS:
        parts.append(f'  <resheader name="{name}">')
        parts.append(f"    <value>{value}</value>")
        parts.append("  </resheader>")

    for key in sorted(STRINGS):
        value = STRINGS[key][index]
        escaped = (
            value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        )
        parts.append(f'  <data name="{key}" xml:space="preserve">')
        parts.append(f"    <value>{escaped}</value>")
        parts.append("  </data>")

    parts.append("</root>")
    return "\n".join(parts) + "\n"


def main() -> None:
    out_dir = Path(__file__).resolve().parent.parent / "src" / "HAMMOR.App" / "Localization"
    out_dir.mkdir(parents=True, exist_ok=True)

    for filename, index in (("Strings.resx", 0), ("Strings.ar.resx", 1)):
        target = out_dir / filename
        target.write_text(build(index), encoding="utf-8")
        print(f"wrote {target} ({len(STRINGS)} strings)")

    # Validate both files parse as XML before declaring success.
    for filename in ("Strings.resx", "Strings.ar.resx"):
        ET.parse(out_dir / filename)
    print("both files parse as valid XML")


if __name__ == "__main__":
    main()
