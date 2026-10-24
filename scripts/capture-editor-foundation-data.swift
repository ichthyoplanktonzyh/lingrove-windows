// Offline Darwin observation only. Windows never links these private Core Foundation symbols.
// Numeric set/property identifiers follow CFUniChar.h at the source revision recorded in NOTICE.
import Foundation
import CoreFoundation
@_silgen_name("CFUniCharIsMemberOf") func member(_ c:UInt32,_ set:UInt32)->Bool
@_silgen_name("CFUniCharGetUnicodePropertyDataForPlane") func properties(_ kind:UInt32,_ plane:UInt32)->UnsafePointer<UInt8>?
let planes=(0..<17).map{properties(0,UInt32($0))}
func combining(_ value:UInt32)->UInt8{guard let p=planes[Int(value>>16)]else{return 0};let c=Int(value&0xffff);let v=Int(p[c>>8]);return v>0 ? p[256+(v-1)*256+(c&255)] : 0}
var classes:[[UInt32]]=[]
var forwardMappings:[[Any]]=[];var prepend:[[UInt32]]=[];var folds:[[Any]]=[];var extends:[[UInt32]]=[];var nonbase:[[UInt32]]=[];var letters:[[UInt32]]=[]
func add(_ c:UInt32,_ list:inout [[UInt32]]) {if let last=list.last,last[1]+1 == c {list[list.count-1][1]=c}else{list.append([c,c])}}
for value in UInt32(0)...0x10ffff {
 guard let scalar=Unicode.Scalar(value)else{continue}
 let cc=combining(value);if cc>0{classes.append([value,UInt32(cc)])}
 let text=String(scalar);var folded=text.folding(options:[.caseInsensitive,.diacriticInsensitive],locale:nil)
 let anchor=text+"A"
 if anchor.first!.utf16.count>text.utf16.count && (anchor as NSString).rangeOfComposedCharacterSequence(at:0).length==text.utf16.count {add(value,&prepend)}
 let compatibility=folded.decomposedStringWithCompatibilityMapping
 if compatibility != folded {
  let forward=(text as NSString).range(of:compatibility,options:[.caseInsensitive,.diacriticInsensitive])
  let backward=(compatibility as NSString).range(of:text,options:[.caseInsensitive,.diacriticInsensitive])
  if forward.location==0 && forward.length==text.utf16.count { if backward.location==0 && backward.length==compatibility.utf16.count {folded=compatibility} else {forwardMappings.append([value,compatibility])} }
 }
 if folded != text {folds.append([value,folded])}
 if member(value,110){add(value,&extends)}
 if member(value,8){add(value,&nonbase)}
 if member(value,5){add(value,&letters)}
}
let data:[String:Any]=["captureLocale":Locale.current.identifier,"operatingSystem":ProcessInfo.processInfo.operatingSystemVersionString,"coreFoundationVersion":kCFCoreFoundationVersionNumber,"scalarCount":0x110000-0x800,"combiningClasses":classes,"forwardCompatibility":forwardMappings,"prepend":prepend,"folds":folds,"graphemeExtend":extends,"nonBase":nonbase,"letters":letters]
FileHandle.standardOutput.write(try JSONSerialization.data(withJSONObject:data,options:[.sortedKeys]))
