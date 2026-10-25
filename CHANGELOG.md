# 3.3.0

对地射程改为 1.5 倍，对空改为 4 倍。地面、对空和信号范围分别设置，避免一个全局倍率把所有范围一起拉大。

- 加长地面射程时，机枪和近程电浆的弹丸寿命跟着延长；超出原射程的直射目标会检查地形和植物遮挡，减少持续空射。
- 增加 Recommended、GalaxyOnly、EnhancedDefense 和 Custom 预设。已有配置保留原值；要使用新的倍率，请选择 Recommended 并重启游戏。
- 出生地保留少量硅、钛和有机晶体，木卫三、木卫四承担更多后续资源供应。已有存档的轨道和矿点保持原样。
- 修正植物密集处的候选查询，避免加长射程后的植被遮挡检查出现数组越界。

Ground range is now 1.5× and space range 4× in the Recommended preset. Ground, space and signal settings are separate.

- Gauss and local-plasma projectile lifetimes follow extended range. Straight shots beyond native range check terrain and plants before firing.
- Added Recommended, GalaxyOnly, EnhancedDefense and Custom presets. Existing configurations retain their values; select Recommended and restart to use the new multipliers.
- Europa keeps small silicon, titanium and organic-crystal deposits. Ganymede and Callisto supply more of the later resources. Existing orbits and deposits stay unchanged.
- Fixed dense-vegetation candidate queries used by extended ground-fire obstruction checks.

# 3.2.0 — Public preview

- Real orbital-distance ratios for the eight major planets and a 23-body starting system.
- Radius-200 construction worlds, 50/300 solid worlds, large gas giants and distributed starting resources.
- Varied systems, independent defense targeting, travel balance and an in-memory compatibility preloader.
