using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZeoCore
{
    internal sealed class ZeoHelpTopic
    {
        internal string Id,Title,Page,Key,Body;
    }
    // Constructed only when help is opened. No settings writes, network or game reads.
    internal sealed class ZeoHelpCatalog
    {
        internal readonly List<ZeoHelpTopic> Topics=new List<ZeoHelpTopic>();
        internal ZeoHelpCatalog()
        {
            Guide("quick","Quick start / navigating the menu","FLIGHT",
                "Open Core with your configured MENU KEY (normally HOME). The two rows of tabs group related features. PREVIOUS / NEXT move through settings within a tab; the page counter shows what remains.\n\nON / OFF and dropdown changes save immediately. For typed numbers, use ENTER or APPLY. A validation message means the value was not saved. Profiles change several settings together.\n\nClick ? on the outer header for help with the current tab. Search a feature or setting name, select a result, then OPEN SETTINGS. This jumps to the correct settings page without changing its values. BACK returns to your menu.\n\nHUD layout editing is different: drag/resize a draft and choose SAVE. CANCEL discards that draft; it does not undo ordinary menu settings.");
            Guide("tab-FLIGHT","Flight / show or hide HUD groups","FLIGHT",
                "HUD enabled controls the external display. Turn individual Ship Status, Tracks on Scope and FleetLink panels on or off here. Ship resources, health and speed can be selected individually.\n\nNames, distances and closing-speed labels add detail to markers. To move a panel or change its text size, use LAYOUT. To change signal size, use MARKERS. The HUD uses the controlled ship; some ship readings are unavailable on foot.");
            Guide("tab-SCOPE","Scope / local sensors and performance","SCOPE",
                "Choose local Spectrum and WeaponCore sources, hostile contacts, and neutral/unknown contacts. TOS capacity limits the table; Max contact markers limits world markers.\n\nFast camera updates and native Spectrum anchoring keep markers aligned while looking around. Prediction is bounded motion estimation between observations, not new sensor information.\n\nMax range, declutter, stale timeout and last-known timeout affect visibility. Shared contacts also have FLEET limits. Adaptive processing and the per-source cap bound tactical work; raising caps may cost more in busy fights.");
            Guide("tab-FLEET","Fleet / sharing and friendly contacts","FLEET",
                "Enable Receive shared FleetLink picture, then choose remote friendlies, shared WeaponCore threats and shared Spectrum signals. Sharing your own detections has separate switches. PRIVACY controls telemetry transmission.\n\nMax shared tracks = 0 hides shared tactical contacts. Max shared distance = 0 adds no extra shared-distance cap; other range/sector rules can still apply. Friendly marker count/range controls are separate.\n\nA square is a non-Spectrum shared sensor report; a diamond is shared Spectrum. Matching exact grid/emitter identities merge into one contact. When your own sensors acquire it, the local icon replaces the shared icon while the track number is retained.\n\nAttack designation uses the authenticated target-link feature; see the Attack targets guide.");
            Guide("tab-LAYOUT","HUD layout / move, resize and text","LAYOUT",
                "Choose EDIT HUD LAYOUT. Select a group in the editor dropdown if it is hidden or covered. Drag the body to move; drag an edge for width or height; drag a corner for both.\n\nSAVE commits the draft. CANCEL leaves the previous layout. UNDO ALL restores the opening draft. RESET SIZE restores the selected frame size. Each heading stays centered in its own frame.\n\nPer-box text settings grow text inside the existing frame. A narrow cell may still shrink long text to prevent overlap; widen the frame when needed. MARKERS controls world icons separately.\n\nDISTRESS / SOS is one movable group: active alert details and brief send/confirmation messages share it. Its editor preview is available even without a real alert.");
            Guide("tab-THEME","Theme / custom signal colors","THEME",
                "Choose a preset, or CUSTOM to use your own relationship colors. PICK opens RGB controls; a six-digit hex value can also be entered. HUD text, backing, borders and signal colors have separate controls.\n\nLocal signals use > <; unknown, neutral and hostile colors distinguish relationship. Shared confirmed hostiles use triangles. Friendlies use a green circle with a ship symbol. Focus, stale and attack states can override the normal color.\n\nFRAME and FONT styles are in LAYOUT. Menu-color settings apply to the legacy external menu; Core's native menu keeps its Space Engineers styling.");
            Guide("tab-MARKERS","Markers / size previews and signal legend","MARKERS",
                "Set a preview distance, then adjust each icon or track-ID size. Preview geometry uses screen pixels and the same distance multiplier/cap as the HUD. Menu text uses the native font.\n\nMaximum marker size is a final cap. If a size slider appears to stop growing, raise this cap. Focus and off-screen multipliers also respect it.\n\nSIGNAL LEGEND shows all source and state cues. Local Spectrum, including neutral tracks, keeps > <. Shared Spectrum is a diamond until confirmed hostile, then a triangle. Shared non-Spectrum sensor contacts use a square until confirmed hostile.\n\nFocus is a highlight color. Attack designation adds pulsing corner brackets and ATTACK around the existing icon; it does not classify a neutral as hostile.");
            Guide("tab-PRIVACY","Privacy / transmit and local logging","PRIVACY",
                "Transmit my telemetry controls outgoing telemetry. Receiving shared tracks has its own FLEET switch. Authenticated attack marks also honor transmission being off.\n\nWrite local last-payload.json is a troubleshooting option that saves telemetry locally. Enable it only when you want that local record.\n\nStreamer mode requests capture exclusion for the external HUD and legacy menu on supported capture paths. The native in-game settings/help screens can appear in recordings; capture exclusion does not hide them. Check your recording preview.");
            Guide("tab-CAPTURE","Capture / external overlay and startup","CAPTURE",
                "Auto-launch ZeoOverlay starts Core's external HUD companion. Core keeps the HUD outside the game for supported capture exclusion. The native settings and help screens remain in-game.\n\nHUD and legacy-menu capture exclusions are separate controls; Streamer mode sets both. Capture software support varies, so verify your recording preview.\n\nIf the HUD is missing, check FLIGHT > HUD enabled, the relevant panel switch, overlay auto-launch, and LAYOUT > Follow game HUD. A hidden game HUD can also hide the overlay.");
            Guide("tab-AMMO","Ammunition / display versus loading","AMMO",
                "AMMO configures the ammunition HUD: visibility, relevant/stocked-only filtering, and which ammunition types appear. Hiding an ammo row does not change a loading target.\n\nSet WANT targets and start docked service on REFILL. WANT is a ship-wide amount, including stock in cockpits, connectors and other accessible inventories. Existing compatible ammo above WANT is retained, not automatically trimmed.\n\nIf the panel is empty, control a ship and check relevant-only/type visibility. Inventory readings depend on the next scan.");
            Guide("tab-ROSTER","Roster / fleet and other sectors","ROSTER",
                "Enable the fleet roster and choose its row limit. Show allies in other sectors allows roster entries outside the local tactical view. A roster entry is not proof of a current local sensor observation.\n\nFriendly world markers, friendly count/range and receiving data are controlled in FLEET. Sector and observation freshness affect visibility.\n\nMove or resize the roster in LAYOUT; its text has a separate per-box scale. Missing/stale data cannot be fixed by increasing icon size.");
            Guide("tab-DISTRESS","Distress / send, clear and navigation","DISTRESS",
                "Enable the distress hotkey, choose its key, type, visibility, hold duration and lifetime. Control a ship in a game faction, then hold the key to send. Hold again while your distress is active to clear it.\n\nThe DISTRESS / SOS box shows sending, confirmed or failed status. Sending is not confirmation. Active received alerts keep their details visible while your brief status appears beneath them.\n\nMove the combined box in LAYOUT > EDIT HUD LAYOUT. World pings and cross-sector alerts have separate switches. Approved sharing rules still determine who receives a call; Alliance selection does not create an alliance.\n\nReceived distress GPS entries are saved without showing on the game HUD, including cross-sector calls, for navigation tools to use. Navigation requires valid received coordinates.");
            Guide("tab-REFILL","Refill / load, unload and tanks","REFILL",
                "Dock to a connected base with usable inventory access. Set compatible ammunition WANT amounts and enable ammo, tanks and/or native fusion pellets. Start with the refill button or configured key.\n\nOnly ammunition for supported weapons installed on this ship is imported. Onboard stock feeds weapons first, then base supplies; reserves prefer real reinforced cargo after weapons. Native SDX2 fuel and oxygen/fuel gas tanks have separate service controls.\n\nUnload non-target cargo is optional. It checks all accessible ship block inventory slots, including connectors, cockpits and machines. Name a block [ZEO KEEP] to protect all its slots. Configured compatible ammo, unknown ammo and native fuel remain protected.\n\nKnown zero-WANT or incompatible ammo can unload. Excess compatible ammo above WANT is kept. Confirmed moves per pass and units per transfer control throughput; only one unconfirmed request may wait at once.");
            Guide("attack","Mark an attack target / allies","FLEET",
                "Enable authenticated target marks and set Mark / clear target key. Use TARGET LINK STATUS / CLEAR to see the device pairing code; link it through the website's Members > ZeoCore Devices. Approved faction information access is required.\n\nAim near a fresh eligible signal and press the key. Press on another to replace it, or on the same target/empty space to clear. The mark expires after 60 seconds. It adds pulsing corners and ATTACK while preserving the source icon and track number.\n\nRecipients must be in the same sector and within their shared-track limits. Allies require explicit opt-in, effective Alliance access and approved contact-sharing contribution; a distress-only grant is insufficient. A mark is intent, not a weapon command or hostility confirmation.","TargetMarksEnabled");
            Guide("binding","Set a keybind / click, press, Apply","REFILL",
                "For Quick Refill or Mark / clear target, click the key field to listen, press the desired key/modifier combination, then APPLY. CLEAR followed by APPLY removes the binding. Leaving without APPLY discards the draft binding.\n\nConflicts with menu, distress and the other action are rejected. These actions do not fire while menus/chat or key capture are active. MENU KEY at the top of every tab now opens a press-to-set editor: click the key, press the desired key, then APPLY. CLEAR and APPLY disables it; Pulsar Configure can reopen the menu. ESC / CANCEL discards the draft. Distress keeps its own selection control.\n\nQuick Refill is in REFILL. Target marking is in FLEET.","QuickRefillKey");
            Guide("missing","Troubleshooting / missing or duplicate tracks","FLEET",
                "Check Receive FleetLink, the correct shared source switches, sector, freshness, max shared count and distance. Max shared tracks = 0 hides shared contacts. Friendly settings are separate.\n\nLocal and shared reports merge when their exact identities match. Nearby positions alone are not proof they are the same ship. Unknown or mismatched identity can prevent a safe merge.\n\nA stale track is a last-known observation, not a live connection. Raising retention keeps old information longer; it does not refresh it. For persistent duplicates or friendly dropouts, note the sector, track IDs and source types when reporting the issue.");
            Guide("sync","Troubleshooting / refill awaiting sync","REFILL",
                "Awaiting sync means the requested inventory transfer has not yet been confirmed at both ends. Do not repeatedly start new runs to force it.\n\nCheck that the connector is connected, the base has free cargo space, and ownership/conveyor access permits the move. Try manually moving one affected stack. Include connector/cockpit contents when checking what remains.\n\n[ZEO KEEP], compatible configured ammo, unknown ammo and native fuel can deliberately remain aboard. Excess compatible ammo is not trimmed to WANT. Reducing moves per pass can help isolate a blocked transfer; it cannot bypass server permissions.");
            foreach(var option in ZeoNativeCatalog.Options)
            {
                string how=option.Kind==NativeOptionKind.Boolean?"Choose ON or OFF; the change saves immediately.":option.Kind==NativeOptionKind.Choice?"Select a dropdown value; the change saves immediately.":option.Kind==NativeOptionKind.Color?"Use PICK or enter a six-digit hex color, then APPLY.":"Type a value, then ENTER or APPLY. Invalid values are rejected.";
                if(option.Key=="QuickRefillKey"||option.Key=="TargetMarkKey")how="Click to listen, press a key/modifier combination, then APPLY. CLEAR + APPLY unbinds it.";
                if(option.Kind==NativeOptionKind.Number&&option.Key!="TargetMarkKey")how+="\nAllowed range: "+option.Min.ToString(CultureInfo.InvariantCulture)+" to "+option.Max.ToString(CultureInfo.InvariantCulture)+".";
                if(option.Choices!=null&&option.Key!="QuickRefillKey")how+="\nChoices: "+string.Join(", ",option.Choices)+".";
                var guide=Topics.First(t=>t.Id=="tab-"+option.Page);
                Topics.Add(new ZeoHelpTopic{Id=option.Page+":"+option.Key,Title=option.Label,Page=option.Page,Key=option.Key,Body=option.Section+"\n\n"+how+"\n\n"+guide.Body});
            }
        }
        private void Guide(string id,string title,string page,string body,string key=null){Topics.Add(new ZeoHelpTopic{Id=id,Title=title,Page=page,Key=key,Body=body});}
        internal List<ZeoHelpTopic> Search(string query)
        {
            var words=(query??"").Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
            return Topics.Where(t=>words.All(w=>(t.Title+" "+t.Page+" "+t.Body).IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderByDescending(t=>!string.IsNullOrWhiteSpace(query)&&t.Title.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
        }
        internal static int ViewFor(ZeoHelpTopic topic,int rowsPerView)
        {
            var options=ZeoNativeCatalog.Options.Where(o=>o.Page==topic.Page).ToArray();
            int index=Array.FindIndex(options,o=>o.Key==topic.Key);return Math.Max(0,index)/Math.Max(1,rowsPerView);
        }
        internal static List<string> Wrap(string text,int width)
        {
            var lines=new List<string>();width=Math.Max(10,width);
            foreach(string paragraph in (text??"").Replace("\r","").Split('\n')){
                string rest=paragraph;
                while(rest.Length>width){int split=rest.LastIndexOf(' ',width);if(split<1)split=width;lines.Add(rest.Substring(0,split));rest=rest.Substring(split).TrimStart();}
                lines.Add(rest);
            }return lines;
        }
    }
}
