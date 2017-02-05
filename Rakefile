
#
#
#

MONO = 'mono'
CSLUA = 'bin/Debug/cslua.exe'

task :test do
  files = ['code.lua', 'vararg.lua']
  files.each do |f|
    sh MONO, CSLUA, '../mlua/luatest/' + f
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
    sh 'time', 'mono', 'bin/Debug/cslua.exe', f
    # sh 'time', 'mono', 'bin/Release/cslua.exe', f
    # sh 'time', 'ruby', '../mlua/mlua.rb', f
  end
end
