
#
#
#

if RUBY_PLATFORM =~ /darwin/
  MONO = ['mono']
  LUA = './lua54'
else
  MONO = []
  LUA = 'lua54.exe'
end
TLUA = 'bin/Debug/cslua.exe'
TEST_DIR = 'lua-5.3.4-tests'

task :test do
  files = ['code.lua', 'vararg.lua']
  files.each do |f|
    sh MONO, TLUA, '../mlua/luatest/' + f
  end
end

task :bench do
  [
    'empty.lua',
    'fib.lua',
    # '/Users/makoto/mlua/luatest/code.lua', '/Users/makoto/mlua/luatest/vararg.lua'
  ].each do |f|
    puts '='*80
    puts f
    sh 'time', 'lua5.3', f
    sh 'time', *'mono', 'bin/Debug/cslua.exe', f
    # sh 'time', 'mono', 'bin/Release/cslua.exe', f
    # sh 'time', 'ruby', '../mlua/mlua.rb', f
  end
end

task :test_lua54 do
  Dir.chdir(TEST_DIR) do
    sh '../lua54.exe', '-e', '_U=true', 'all.lua'
  end
end

task :luac do
  mkdir_p 'tmp/luac'
  FileList[TEST_DIR+'/*.lua'].each do |f|
    luac = "tmp/luac/#{File.basename(f,'.lua')}.luac"
    luac_txt = "tmp/luac/#{File.basename(f,'.lua')}.txt"
    sh( LUA, 'luac.lua', f, out: luac)
    sh( *MONO, TLUA, luac, out: luac_txt)
  end
end

task :tluac do
  mkdir_p 'tmp/luac'
  FileList[TEST_DIR+'/*.lua'].each do |f|
    luac_txt = "tmp/luac/#{File.basename(f,'.lua')}_t.txt"
    sh( *MONO, TLUA, f)
    #sh( *MONO, TLUA, f, out: luac_txt)
  end
end
