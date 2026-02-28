import {Link, useLocation} from 'react-router-dom'
import {useState, useRef, useEffect} from 'react'
import GlobalSearch from '../GlobalSearch/GlobalSearch'
import ThemeToggle from '../ThemeToggle/ThemeToggle'

const Navigation = () => {
    const location = useLocation()
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
    const [sidebarCollapsed, setSidebarCollapsed] = useState(() => {
        try {
            const saved = localStorage.getItem('sidebarCollapsed')
            return saved ? JSON.parse(saved) : false
        } catch {
            return false
        }
    })
    const menuRef = useRef<HTMLDivElement>(null)
    const hamburgerRef = useRef<HTMLButtonElement>(null)

    useEffect(() => {
        localStorage.setItem('sidebarCollapsed', JSON.stringify(sidebarCollapsed))
        globalThis.dispatchEvent(new Event('sidebarToggle'))
    }, [sidebarCollapsed])

    const toggleSidebar = () => {
        setSidebarCollapsed(!sidebarCollapsed)
    }


    useEffect(() => {
        setMobileMenuOpen(false)
    }, [location.pathname])

    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (
                mobileMenuOpen &&
                menuRef.current &&
                !menuRef.current.contains(event.target as Node) &&
                hamburgerRef.current &&
                !hamburgerRef.current.contains(event.target as Node)
            ) {
                setMobileMenuOpen(false)
            }
        }

        if (mobileMenuOpen) {
            document.addEventListener('mousedown', handleClickOutside)
            return () => document.removeEventListener('mousedown', handleClickOutside)
        }
    }, [mobileMenuOpen])

    useEffect(() => {
        const handleEscape = (event: KeyboardEvent) => {
            if (event.key === 'Escape' && mobileMenuOpen) {
                setMobileMenuOpen(false)
                hamburgerRef.current?.focus()
            }
        }

        if (mobileMenuOpen) {
            document.addEventListener('keydown', handleEscape)
            return () => document.removeEventListener('keydown', handleEscape)
        }
    }, [mobileMenuOpen])

    const toggleMobileMenu = () => {
        setMobileMenuOpen(!mobileMenuOpen)
    }

    const closeMobileMenu = () => {
        setMobileMenuOpen(false)
    }

    const navItems = [
        {path: '/', label: 'Stats', icon: '📊'},
        {path: '/players', label: 'Players', icon: '👥'},
        {path: '/matches', label: 'Matches', icon: '⚔️'},
        {path: '/teams', label: 'Teams', icon: '👫'},
        {path: '/leaderboards', label: 'Leaderboards', icon: '🏆'},
        {path: '/builds', label: 'Builds', icon: '📦'},
        {path: '/highlights', label: 'Highlights', icon: '⭐'},
        {path: '/discover', label: 'Discover', icon: '🔍'},
        {path: '/favorites', label: 'Favorites', icon: '⭐'},
        {path: '/rivals', label: 'Rivals', icon: '⚡'},
        {path: '/profile', label: 'Profile', icon: '👤'},
        {path: '/upload', label: 'Upload', icon: '📤'},
    ]

    const renderNavItem = (item: {
        path: string,
        label: string,
        icon: string
    }, onClick?: () => void, collapsed?: boolean) => {
        const isActive = item.path === '/'
            ? location.pathname === '/'
            : location.pathname.startsWith(item.path)
        return (
            <Link
                key={item.path}
                to={item.path}
                onClick={onClick}
                title={collapsed ? item.label : undefined}
                className={`flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-semibold transition-colors w-full ${
                    collapsed ? 'justify-center' : ''
                } ${
                    isActive
                        ? 'bg-gradient-to-r from-accent to-sky-400 text-white shadow-lg'
                        : 'text-text-muted hover:text-text hover:bg-surface/50'
                }`}
            >
                <span className="text-lg">{item.icon}</span>
                {!collapsed && <span>{item.label}</span>}
            </Link>
        )
    }

    return (
        <>
            <div className="sticky top-0 z-50 border-b border-accent-muted/30 bg-background/95 backdrop-blur-sm">
                <div className="container mx-auto px-4 py-3 flex items-center gap-3">
                    <div className="flex-1">
                        <GlobalSearch/>
                    </div>
                    <div className="hidden lg:block">
                        <ThemeToggle/>
                    </div>
                </div>
            </div>

            <div className="lg:hidden">
                <div
                    className="sticky top-[73px] z-40 border-b border-accent-muted/30 bg-background/95 backdrop-blur-sm">
                    <div className="flex items-center justify-center px-4 py-3">
                        <button
                            ref={hamburgerRef}
                            type="button"
                            onClick={toggleMobileMenu}
                            aria-expanded={mobileMenuOpen}
                            aria-controls="mobile-menu"
                            aria-label="Toggle navigation menu"
                            className="flex items-center justify-center p-2 rounded-lg text-text-muted hover:text-text hover:bg-surface/50 transition-colors"
                        >
                            <svg
                                className={`h-6 w-6 transition-transform duration-300 ${mobileMenuOpen ? 'rotate-90' : ''}`}
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                                xmlns="http://www.w3.org/2000/svg"
                            >
                                {mobileMenuOpen ? (
                                    <path
                                        strokeLinecap="round"
                                        strokeLinejoin="round"
                                        strokeWidth={2}
                                        d="M6 18L18 6M6 6l12 12"
                                    />
                                ) : (
                                    <path
                                        strokeLinecap="round"
                                        strokeLinejoin="round"
                                        strokeWidth={2}
                                        d="M4 6h16M4 12h16M4 18h16"
                                    />
                                )}
                            </svg>
                        </button>
                    </div>
                </div>

                {mobileMenuOpen && (
                    <>
                        <div
                            className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40 lg:hidden"
                            onClick={closeMobileMenu}
                            aria-hidden="true"
                        />
                        <div
                            ref={menuRef}
                            id="mobile-menu"
                            className="fixed left-0 top-[73px] bottom-0 w-64 z-50 border-r border-accent-muted/30 bg-background/95 backdrop-blur-sm shadow-xl transform transition-transform duration-300 ease-in-out lg:hidden"
                        >
                            <div className="flex flex-col h-full px-4 py-4 gap-2 overflow-y-auto">
                                <div className="flex justify-end pb-2 border-b border-accent-muted/30 mb-2">
                                    <ThemeToggle/>
                                </div>
                                {navItems.map((item) => renderNavItem(item, closeMobileMenu))}
                            </div>
                        </div>
                    </>
                )}
            </div>

            <aside
                className={`hidden lg:flex fixed left-0 top-[73px] bottom-0 z-40 border-r border-accent-muted/30 bg-background/95 backdrop-blur-sm flex-col transition-all duration-300 ${
                    sidebarCollapsed ? 'w-20' : 'w-64'
                }`}>
                <div className={`flex items-center border-b border-accent-muted/30 p-4 gap-2 ${
                    sidebarCollapsed ? 'justify-center' : 'justify-end'
                }`}>
                    <button
                        type="button"
                        onClick={toggleSidebar}
                        aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                        className={`p-2 rounded-lg text-text-muted hover:text-text hover:bg-surface/50 transition-colors`}
                    >
                        <svg
                            className={`h-5 w-5 transition-transform duration-300 ${sidebarCollapsed ? '' : 'rotate-180'}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={2}
                                d="M11 19l-7-7 7-7m8 14l-7-7 7-7"
                            />
                        </svg>
                    </button>
                </div>

                <nav className="flex-1 overflow-y-auto px-2 py-4">
                    <div className="flex flex-col gap-2">
                        {navItems.map((item) => renderNavItem(item, undefined, sidebarCollapsed))}
                    </div>
                </nav>
            </aside>
        </>
    )
}

export default Navigation
