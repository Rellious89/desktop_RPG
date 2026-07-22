import AppKit
import ApplicationServices

struct Config: Decodable {
    var Width = 221.0, Height = 30.0, VisualScale = 0.95, HorizontalOffset = 0.0, BottomOffset = 32.0
    var TrackingIntervalMs = 8, ResizeSettleMs = 120, ComposerMissingGraceMs = 500, ExitAfterAppCloseMs = 1000
    var UsageRefreshIntervalMs = 60000, UsageMaxAttempts = 3, UsageRetryDelayMs = 5000
}

struct Usage { var five: Int?; var week: Int? }

final class MeterView: NSView {
    var usage = Usage(five: nil, week: nil) { didSet { needsDisplay = true } }
    var dark = true { didSet { needsDisplay = true } }
    override var isOpaque: Bool { false }
    func color(_ percent: Int) -> NSColor {
        let p = CGFloat(max(1, min(100, percent)))
        let a: (CGFloat,CGFloat,CGFloat), b: (CGFloat,CGFloat,CGFloat), t: CGFloat
        if p <= 50 { a=(0xEF,0x44,0x44); b=(0xEA,0xB3,0x08); t=(p-1)/49 }
        else { a=(0xEA,0xB3,0x08); b=(0x22,0xC5,0x5E); t=(p-50)/50 }
        return NSColor(red:(a.0+(b.0-a.0)*t)/255, green:(a.1+(b.1-a.1)*t)/255, blue:(a.2+(b.2-a.2)*t)/255, alpha:1)
    }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.setFill(); dirtyRect.fill()
        let scale: CGFloat = 0.95, itemW: CGFloat = 101 * scale, actualW = itemW * 0.9
        let titleColor = dark ? NSColor(calibratedWhite:166/255, alpha:1) : NSColor(calibratedWhite:105/255, alpha:1)
        let valueColor = dark ? NSColor(calibratedWhite:236/255, alpha:1) : NSColor(calibratedWhite:36/255, alpha:1)
        let emptyColor = dark ? NSColor(calibratedWhite:145/255, alpha:1) : NSColor(calibratedWhite:126/255, alpha:1)
        let track = dark ? NSColor(calibratedWhite:75/255, alpha:1) : NSColor(calibratedWhite:218/255, alpha:1)
        for (i, pair) in [("5h",usage.five),("7d",usage.week)].enumerated() {
            let logicalX: CGFloat = i == 0 ? 0 : 120 * scale
            let x = logicalX + (itemW-actualW)/2, y: CGFloat = bounds.height - 13
            let titleAttrs: [NSAttributedString.Key:Any] = [.font:NSFont.systemFont(ofSize:10*scale),.foregroundColor:titleColor]
            let valueAttrs: [NSAttributedString.Key:Any] = [.font:NSFont.systemFont(ofSize:11*scale,weight:.bold),.foregroundColor:pair.1 == nil ? emptyColor : valueColor]
            NSString(string:pair.0).draw(at:NSPoint(x:x,y:y),withAttributes:titleAttrs)
            let value = pair.1.map { "\($0)%" } ?? "--", size = NSString(string:value).size(withAttributes:valueAttrs)
            NSString(string:value).draw(at:NSPoint(x:x+actualW-size.width,y:y),withAttributes:valueAttrs)
            let barY = bounds.height - 19, h = max(1,round(2.5*scale))
            track.setFill(); NSBezierPath(roundedRect:NSRect(x:x,y:barY,width:actualW,height:h),xRadius:h/2,yRadius:h/2).fill()
            if let p=pair.1, p>0 { color(p).setFill(); NSBezierPath(roundedRect:NSRect(x:x,y:barY,width:actualW*CGFloat(p)/100,height:h),xRadius:h/2,yRadius:h/2).fill() }
        }
    }
}

final class UsageReader: @unchecked Sendable {
    func read() async -> Usage? {
        let process=Process(), input=Pipe(), output=Pipe()
        process.executableURL=URL(fileURLWithPath:"/opt/homebrew/bin/codex")
        process.arguments=["app-server","--stdio"]
        process.standardInput=input; process.standardOutput=output; process.standardError=FileHandle.nullDevice
        do { try process.run() } catch { return nil }
        let initLine="{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-usage-overlay\",\"version\":\"1.0\"}}}\n"
        let readLine="{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}\n"
        input.fileHandleForWriting.write(Data(initLine.utf8))
        var sentRead = false
        do {
            for try await line in output.fileHandleForReading.bytes.lines {
                guard let d=line.data(using:.utf8), let root=try? JSONSerialization.jsonObject(with:d) as? [String:Any] else { continue }
                if (root["id"] as? Int)==1, !sentRead { input.fileHandleForWriting.write(Data(readLine.utf8)); sentRead=true; continue }
                guard (root["id"] as? Int)==2, let result=root["result"] as? [String:Any], let limits=result["rateLimits"] as? [String:Any] else { continue }
                func remaining(_ key:String)->Int? { guard let w=limits[key] as? [String:Any], let used=w["usedPercent"] as? Int else{return nil}; return max(0,min(100,100-used)) }
                process.terminate(); return Usage(five:remaining("primary"),week:remaining("secondary"))
            }
        } catch {}
        process.terminate(); return nil
    }
}

@MainActor final class Controller: NSObject, NSApplicationDelegate {
    var cfg=Config(), panel:NSPanel!, view=MeterView(), timer:Timer?, lastFound=Date.distantPast, target:NSRunningApplication?
    var lastDiagnosticWrite = Date.distantPast, lastDiscovery = Date.distantPast, lastComposerFrame: CGRect?, lastWindowFrame: CGRect?
    let reader=UsageReader(), bundleID="com.openai.codex"
    func applicationDidFinishLaunching(_ n:Notification) {
        NSApp.setActivationPolicy(.accessory)
        if let url=Bundle.main.url(forResource:"config",withExtension:"json"), let d=try? Data(contentsOf:url), let c=try? JSONDecoder().decode(Config.self,from:d){cfg=c}
        panel=NSPanel(contentRect:NSRect(x:0,y:0,width:cfg.Width*cfg.VisualScale,height:cfg.Height*cfg.VisualScale),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        panel.level = .floating; panel.isOpaque=false; panel.backgroundColor = .clear; panel.hasShadow=false; panel.ignoresMouseEvents=true; panel.hidesOnDeactivate=false; panel.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary,.ignoresCycle]; panel.contentView=view
        NSWorkspace.shared.notificationCenter.addObserver(self,selector:#selector(workspaceChanged),name:NSWorkspace.didLaunchApplicationNotification,object:nil)
        NSWorkspace.shared.notificationCenter.addObserver(self,selector:#selector(workspaceChanged),name:NSWorkspace.didTerminateApplicationNotification,object:nil)
        workspaceChanged(); timer=Timer.scheduledTimer(withTimeInterval:Double(cfg.TrackingIntervalMs)/1000,repeats:true){[weak self]_ in MainActor.assumeIsolated { self?.track() }}
        Task { await refreshLoop() }
    }
    @objc func workspaceChanged(){ target=NSWorkspace.shared.runningApplications.first{$0.bundleIdentifier==bundleID} }
    func attr(_ el:AXUIElement,_ key:String)->AnyObject? { var v:CFTypeRef?; return AXUIElementCopyAttributeValue(el,key as CFString,&v) == .success ? v : nil }
    func frame(_ el:AXUIElement)->CGRect? { guard let po=attr(el,kAXPositionAttribute), let so=attr(el,kAXSizeAttribute) else{return nil}; let p=po as! AXValue, s=so as! AXValue; var pt=CGPoint.zero,sz=CGSize.zero; AXValueGetValue(p,.cgPoint,&pt); AXValueGetValue(s,.cgSize,&sz); return CGRect(origin:pt,size:sz) }
    func findComposer(_ root:AXUIElement,depth:Int=0)->CGRect? {
        if depth>32{return nil}; let role=attr(root,kAXRoleAttribute) as? String
        let editable = (attr(root,"AXEditable") as? Bool) == true
        if (role==kAXTextAreaRole || role==kAXTextFieldRole || editable), let f=frame(root), f.width>200, f.height>10, f.height<300 {
            var e=root, best=f
            for _ in 0..<7 {
                guard let po=attr(e,kAXParentAttribute) else { break }; let p=po as! AXUIElement
                if let pf=frame(p), pf.width>=f.width, pf.width<1800, pf.height>=f.height, pf.height<300 { best=pf; e=p } else { break }
            }
            return best
        }
        for child in ((attr(root,kAXChildrenAttribute) as? [AXUIElement]) ?? []).reversed() { if let f=findComposer(child,depth:depth+1){return f} }; return nil
    }
    func findComposerPlaceholder(_ root:AXUIElement, depth:Int=0)->CGRect? {
        if depth>40 { return nil }
        let keys=[kAXDescriptionAttribute,kAXTitleAttribute,kAXValueAttribute,kAXHelpAttribute]
        let values=keys.compactMap { attr(root,$0) as? String }.map { $0.lowercased().trimmingCharacters(in:.whitespacesAndNewlines) }
        let matches=values.contains("work with chatgpt") || values.contains("message chatgpt") || values.contains("무엇이든 요청하세요")
        if matches, let base=frame(root) {
            var e=root, best=base
            for _ in 0..<10 {
                guard let po=attr(e,kAXParentAttribute) else { break }; let p=po as! AXUIElement
                guard let pf=frame(p) else { break }
                if pf.width>=base.width, pf.width>400, pf.height>=60, pf.height<260 { best=pf; e=p } else { e=p }
            }
            return best
        }
        for child in ((attr(root,kAXChildrenAttribute) as? [AXUIElement]) ?? []).reversed() {
            if let f=findComposerPlaceholder(child,depth:depth+1) { return f }
        }
        return nil
    }
    func findBottomEditable(_ root:AXUIElement, depth:Int=0, best:inout CGRect?) {
        if depth>40 { return }
        let role=attr(root,kAXRoleAttribute) as? String
        let editable=(attr(root,"AXEditable") as? Bool)==true
        if (role==kAXTextAreaRole || role==kAXTextFieldRole || editable), let f=frame(root), f.width>200, f.height>10, f.height<300 {
            var e=root, candidate=f
            for _ in 0..<7 {
                guard let po=attr(e,kAXParentAttribute) else { break }; let p=po as! AXUIElement
                guard let pf=frame(p) else { break }
                if pf.width>=f.width, pf.width<1800, pf.height>=f.height, pf.height<300 { candidate=pf; e=p } else { break }
            }
            if best == nil || candidate.maxY > best!.maxY { best=candidate }
        }
        for child in (attr(root,kAXChildrenAttribute) as? [AXUIElement]) ?? [] { findBottomEditable(child,depth:depth+1,best:&best) }
    }
    func track(){
        guard let app=target, app.isActive, !app.isTerminated else { panel.orderOut(nil); writeDiagnostic(); return }
        let root=AXUIElementCreateApplication(app.processIdentifier)
        if let window=mainWindowFrame(root) {
            if let previous=lastWindowFrame, var cached=lastComposerFrame {
                cached.origin.x += window.maxX-previous.maxX
                cached.origin.y += window.maxY-previous.maxY
                lastComposerFrame=cached
            }
            lastWindowFrame=window
            if Date().timeIntervalSince(lastDiscovery)>=0.04 {
                lastDiscovery=Date()
                if let detected=findBottomComposer(root,window:window) { lastComposerFrame=detected; lastFound=Date() }
            }
            let w=cfg.Width*cfg.VisualScale,h=cfg.Height*cfg.VisualScale
            if let composer=lastComposerFrame, let placement=outsideTopRight(composer:composer,panelWidth:w,panelHeight:h) {
                panel.setContentSize(NSSize(width:w,height:h)); panel.setFrameTopLeftPoint(placement); panel.orderFrontRegardless()
            }
        }
        else if Date().timeIntervalSince(lastFound)>Double(cfg.ComposerMissingGraceMs)/1000 { panel.orderOut(nil) }
        writeDiagnostic()
    }
    func findBottomComposer(_ root:AXUIElement, window:CGRect)->CGRect? {
        var candidates:[CGRect]=[]
        func walk(_ element:AXUIElement,_ depth:Int) {
            if depth>40 { return }
            let role=attr(element,kAXRoleAttribute) as? String
            let editable=(attr(element,"AXEditable") as? Bool)==true
            let keys=[kAXDescriptionAttribute,kAXTitleAttribute,kAXValueAttribute,kAXHelpAttribute]
            let values=keys.compactMap { attr(element,$0) as? String }.map { $0.lowercased().trimmingCharacters(in:.whitespacesAndNewlines) }
            let placeholder=values.contains("work with chatgpt") || values.contains("message chatgpt") || values.contains("무엇이든 요청하세요")
            if role==kAXTextAreaRole || role==kAXTextFieldRole || editable || placeholder {
                var e=element
                for _ in 0..<10 {
                    if let f=frame(e), f.width>=window.width*0.45, f.width<=window.width*0.72, f.height>=50, f.height<500,
                       f.minX>=window.minX, f.maxX<=window.maxX+2, f.maxY>=window.maxY-260, f.maxY<=window.maxY+2 { candidates.append(f) }
                    guard let po=attr(e,kAXParentAttribute) else { break }; e=po as! AXUIElement
                }
            }
            for child in (attr(element,kAXChildrenAttribute) as? [AXUIElement]) ?? [] { walk(child,depth+1) }
        }
        walk(root,0)
        return candidates.max {
            let aBottom=abs(window.maxY-$0.maxY), bBottom=abs(window.maxY-$1.maxY)
            if abs(aBottom-bBottom)>2 { return aBottom>bBottom }
            return $0.height<$1.height
        }
    }
    func outsideTopRight(composer f:CGRect,panelWidth w:CGFloat,panelHeight h:CGFloat)->NSPoint? {
        let desiredAXLeft=f.maxX-w
        let desiredAXTop=f.minY-h-8
        for screen in NSScreen.screens {
            guard let number=screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { continue }
            let displayBounds=CGDisplayBounds(CGDirectDisplayID(number.uint32Value))
            guard displayBounds.contains(CGPoint(x:f.midX,y:f.midY)) else { continue }
            return NSPoint(x:screen.frame.minX+(desiredAXLeft-displayBounds.minX), y:screen.frame.maxY-(desiredAXTop-displayBounds.minY))
        }
        return nil
    }
    func mainWindowFrame(_ root:AXUIElement)->CGRect? {
        let windows=(attr(root,kAXWindowsAttribute) as? [AXUIElement]) ?? []
        return windows.compactMap { frame($0) }.filter { $0.width>600 && $0.height>500 }.max { $0.width*$0.height < $1.width*$1.height }
    }
    func fixedOutsideTopRight(window f:CGRect,panelWidth w:CGFloat,panelHeight h:CGFloat)->NSPoint? {
        // Stable geometry: align with the composer's right edge and sit just above it.
        let desiredAXLeft=f.maxX-140-w
        let desiredAXTop=f.maxY-106-h-8
        for screen in NSScreen.screens {
            guard let number=screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { continue }
            let displayBounds=CGDisplayBounds(CGDirectDisplayID(number.uint32Value))
            guard displayBounds.contains(CGPoint(x:f.midX,y:f.midY)) else { continue }
            let x=screen.frame.minX+(desiredAXLeft-displayBounds.minX)
            let y=screen.frame.maxY-(desiredAXTop-displayBounds.minY)
            return NSPoint(x:x,y:y)
        }
        return nil
    }
    func panelTopLeft(composer f:CGRect, panelWidth w:CGFloat)->NSPoint? {
        for screen in NSScreen.screens {
            guard let number=screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { continue }
            let displayBounds=CGDisplayBounds(CGDirectDisplayID(number.uint32Value))
            guard displayBounds.contains(CGPoint(x:f.midX,y:f.midY)) else { continue }
            let x=screen.frame.minX+(f.midX-displayBounds.minX)-w/2+cfg.HorizontalOffset
            let desiredAXTop=f.maxY-cfg.BottomOffset
            let y=screen.frame.maxY-(desiredAXTop-displayBounds.minY)
            return NSPoint(x:x,y:y)
        }
        return nil
    }
    func writeDiagnostic() {
        guard Date().timeIntervalSince(lastDiagnosticWrite) >= 1 else { return }; lastDiagnosticWrite=Date()
        var d:[String:Any] = ["accessibilityTrusted":AXIsProcessTrusted(),"targetRunning":target != nil,"targetActive":target?.isActive ?? false,"panelVisible":panel?.isVisible ?? false]
        d["display5h"] = view.usage.five.map { "\($0)%" } ?? "--"
        d["display7d"] = view.usage.week.map { "\($0)%" } ?? "--"
        if let f=lastComposerFrame { d["composerFrame"]=["x":Int(f.minX),"y":Int(f.minY),"width":Int(f.width),"height":Int(f.height)] }
        if let data=try? JSONSerialization.data(withJSONObject:d,options:[.prettyPrinted]) { try? data.write(to:URL(fileURLWithPath:"/tmp/codex-usage-overlay-status.json"),options:.atomic) }
    }
    func refreshLoop() async { while true { if target != nil { var next:Usage?; for attempt in 0..<cfg.UsageMaxAttempts { if let u=await reader.read(){next=u;break}; if attempt+1<cfg.UsageMaxAttempts { try? await Task.sleep(for:.milliseconds(cfg.UsageRetryDelayMs)) } }; view.usage=next ?? Usage(five:nil,week:nil) }; try? await Task.sleep(for:.milliseconds(cfg.UsageRefreshIntervalMs)) } }
}

let app=NSApplication.shared, delegate=Controller(); app.delegate=delegate; app.run()
